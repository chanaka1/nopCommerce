using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.CSharp;

namespace Nop.Services.Messages
{
    /// <summary>
    /// Represents the helper for tokenizer
    /// </summary>
    public class TokenHelper
    {
        #region Utilities

        /// <summary>
        /// Replace all of the token key occurences inside the specified template text with corresponded token values wrapped quotes
        /// </summary>
        /// <param name="template">The template with token keys inside</param>
        /// <param name="tokens">The sequence of tokens to use</param>
        /// <returns>Text with all token keys replaces by token value with quotes</returns>
        protected static string ReplaceTokens(string template, IEnumerable<Token> tokens)
        {
            foreach (var token in tokens)
            {
                //wrap the value in quotes
                var tokenValue = string.Format("\"{0}\"", token.Value ?? string.Empty);
                template = template.Replace(string.Format(@"%{0}%", token.Key), tokenValue);
            }

            return template;
        }

        #endregion

        #region Methods

        /// <summary>
        /// Resolve conditional statements and replace them with appropriate values
        /// </summary>
        /// <param name="template">The template with token keys inside</param>
        /// <param name="tokens">The sequence of tokens to use</param>
        /// <returns>Text with all conditional statements replaces by appropriate values</returns>
        public static string ReplaceConditionalStatements(string template, IEnumerable<Token> tokens)
        {
            var regexFullConditionSatement = new Regex(@"(?:(?'Group' %if)|(?'Condition-Group' endif%)|(?! (%if|endif%)).)*(?(Group)(?!))",
                RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.IgnoreCase);
            var regexCondition = new Regex(@"\((?:(?'Group' \()|(?'-Group' \))|[^()])*(?(Group)(?!))\)",
                RegexOptions.Compiled | RegexOptions.IgnorePatternWhitespace | RegexOptions.Singleline | RegexOptions.IgnoreCase);

            //get conditional statemenets in the original template
            var conditions = new List<ConditionalStatement>();
            foreach (Match match in regexFullConditionSatement.Matches(template))
            {
                foreach (Capture capture in match.Groups["Condition"].Captures)
                {
                    var templateCondition = regexCondition.Match(capture.Value).Value;
                    conditions.Add(new ConditionalStatement
                    {
                        Index = capture.Index,
                        FullStatement = capture.Value,
                        TemplateCondition = templateCondition,
                        Condition = ReplaceTokens(templateCondition, tokens)
                    });
                }
            }
            conditions = conditions.OrderBy(condition => condition.Index).ToList();

            if (!conditions.Any())
                return template;

            //dynamic compile conditions in a separate app domain
            var domain = AppDomain.CreateDomain("TokenConditionsDomain");
            var compiler = (TokenConditionsCompiler)domain.CreateInstanceFromAndUnwrap(typeof(TokenConditionsCompiler).Assembly.Location, typeof(TokenConditionsCompiler).FullName);
            var resultConditions = compiler.CompileConditions(conditions.ToDictionary(condition => condition.Index, condition => condition.Condition));
            AppDomain.Unload(domain);

            //replace conditional statements
            foreach (var condition in conditions)
            {
                var conditionIsMet = resultConditions.ContainsKey(condition.Index) && resultConditions[condition.Index];
                template = template.Replace(conditionIsMet ? condition.TemplateCondition : condition.FullStatement, string.Empty).Trim();
            }
            template = template.Replace("%if", string.Empty).Replace("endif%", string.Empty).Trim();

            //return template with resolved conditional statements
            return template;
        }

        #endregion

        #region Nested class

        /// <summary>
        /// Represents conditional statement in templates
        /// </summary>
        [Serializable]
        public class ConditionalStatement
        {
            /// <summary>
            /// Gets or sets the position in the template where the first character of this conditional statement is found.
            /// </summary>
            public int Index { get; set; }

            /// <summary>
            /// Gets or sets full conditional statement
            /// </summary>
            public string FullStatement { get; set; }

            /// <summary>
            /// Gets or sets the original condition (possible with tokens)
            /// </summary>
            public string TemplateCondition { get; set; }

            /// <summary>
            /// Gets or sets the condition that must be met
            /// </summary>
            public string Condition { get; set; }
        }

        #endregion
    }

    /// <summary>
    /// Represents condition compiler
    /// </summary>
    public class TokenConditionsCompiler : MarshalByRefObject
    {
        /// <summary>
        /// Dynamically compile the conditions and return results (are met or not)
        /// </summary>
        /// <param name="conditions">List of conditions</param>
        /// <returns>List of results</returns>
        public Dictionary<int, bool> CompileConditions(Dictionary<int, string> conditions)
        {
            //initially results are not met
            var resultConditions = conditions.ToDictionary(condition => condition.Key, condition => false);
            try
            {
                //compose the code for dynamic compile
                var code = string.Format(@"public static class DynamicCondition {{{0}}}", conditions.Aggregate(string.Empty, (current, next) =>
                    string.Format("{0}{1}", current, string.Format(@"public static bool IsTrue_{0}() {{ return {1}; }}", next.Key, next.Value))));

                //dymamically compile code
                var provider = new CSharpCodeProvider();
                var compilerParameters = new CompilerParameters
                {
                    GenerateInMemory = true,
                    TreatWarningsAsErrors = false,
                    GenerateExecutable = false,
                };
                var compilerResults = provider.CompileAssemblyFromSource(compilerParameters, code);

                if (compilerResults.Errors.HasErrors)
                    return resultConditions;

                //get new class
                var module = compilerResults.CompiledAssembly.GetModules()[0];
                var type = module.GetType("DynamicCondition");

                foreach (var index in conditions.Keys)
                {
                    //find appropriate method 
                    var method = type.GetMethod(string.Format("IsTrue_{0}", index));

                    //then invoke it and save results
                    resultConditions[index] = (bool)method.Invoke(null, null);
                }

                return resultConditions;
            }
            catch (Exception)
            {
                return resultConditions;
            }
        }
    }
}
