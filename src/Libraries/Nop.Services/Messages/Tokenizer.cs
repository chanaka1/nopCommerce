using System;
using System.Collections.Generic;
using System.Web;
using Nop.Core.Domain.Messages;

namespace Nop.Services.Messages
{
    public partial class Tokenizer : ITokenizer
    {
        #region Fields

        private readonly MessageTemplatesSettings _messageTemplatesSettings;

        #endregion

        #region Ctor

        /// <summary>
        /// Ctor
        /// </summary>
        /// <param name="messageTemplatesSettings">Message templates settings</param>
        public Tokenizer(MessageTemplatesSettings messageTemplatesSettings)
        {
            this._messageTemplatesSettings = messageTemplatesSettings;
        }

        #endregion

        #region Utilities

        /// <summary>
        /// Returns a new string in which all occurrences of a specified string in the current instance are replaced with another specified string
        /// </summary>
        /// <param name="original">Origianl string</param>
        /// <param name="pattern">The string to be replaced</param>
        /// <param name="replacement">The string to replace all occurrences of pattern string</param>
        /// <returns>A string that is equivalent to the current string except that all instances of pattern are replaced with replacement string</returns>
        protected string Replace(string original, string pattern, string replacement)
        {
            //for ordinal rules of string comparision use string.Replace() method
            var stringComparison = _messageTemplatesSettings.CaseInvariantReplacement ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (stringComparison == StringComparison.Ordinal)
                return original.Replace(pattern, replacement);

            //or do some routine work here
            replacement = replacement ?? string.Empty;

            var count = 0;
            var position0 = 0;
            var position1 = 0;

            var inc = (original.Length / pattern.Length) * (replacement.Length - pattern.Length);
            var chars = new char[original.Length + Math.Max(0, inc)];
            while ((position1 = original.IndexOf(pattern, position0, stringComparison)) != -1)
            {
                for (int i = position0; i < position1; ++i)
                    chars[count++] = original[i];
                for (int i = 0; i < replacement.Length; ++i)
                    chars[count++] = replacement[i];
                position0 = position1 + pattern.Length;
            }

            if (position0 == 0)
                return original;

            for (int i = position0; i < original.Length; ++i)
                chars[count++] = original[i];

            return new string(chars, 0, count);
        }

        #endregion

        #region Methods

        /// <summary>
        /// Replace all of the token key occurences inside the specified template text with corresponded token values
        /// </summary>
        /// <param name="template">The template with token keys inside</param>
        /// <param name="tokens">The sequence of tokens to use</param>
        /// <param name="htmlEncode">The value indicating whether tokens should be HTML encoded</param>
        /// <returns>Text with all token keys replaces by token value</returns>
        public string Replace(string template, IEnumerable<Token> tokens, bool htmlEncode)
        {
            if (string.IsNullOrWhiteSpace(template))
                throw new ArgumentNullException("template");

            if (tokens == null)
                throw new ArgumentNullException("tokens");

            //replace conditional statements
            template = TokenHelper.ReplaceConditionalStatements(template, tokens);

            foreach (var token in tokens)
            {
                var tokenValue = token.Value;

                //do not encode URLs
                if (htmlEncode && !token.NeverHtmlEncoded)
                    tokenValue = HttpUtility.HtmlEncode(tokenValue);

                template = Replace(template, string.Format(@"%{0}%", token.Key), tokenValue);
            }

            return template;
        }

        #endregion
    }
}
