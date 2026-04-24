using Jablotron.API.SIA.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Jablotron.API.SIA
{
    public sealed class Dc09Parser
    {
        private static readonly Regex IdLinkRegex = new("\"(\\d{4})(L(\\d))?", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TimeRegex = new("(\\d{2}:\\d{2}:\\d{2})", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex DateRegex = new("(\\d{2}-\\d{2}-\\d{4})", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex AccountRegex = new("#(\\d{1,16})\\|", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FirstBracketRegex = new("\\[([^\\]]+)\\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex BracketRegex = new("\\[([^\\]]+)\\]", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FirstBlockRegex = new("^#(\\d{1,16})\\|([^\\^]+)\\^([^\\^]*)\\^([^\\^]*)\\^([^\\^]*)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public Dc09Message ParseHex(string hex)
        {
            if (hex == null)
                throw new ArgumentNullException(nameof(hex));

            return ParseBytes(HexToBytes(hex));
        }

        public Dc09Message ParseBytes(byte[] bytes)
        {
            if (bytes == null)
                throw new ArgumentNullException(nameof(bytes));

            var message = new Dc09Message
            {
                RawBytes = bytes,
                RawText = DecodeWithFallback(bytes)
            };

            var text = message.RawText ?? string.Empty;
            var startIndex = text.IndexOf("SIA", StringComparison.OrdinalIgnoreCase);
            if (startIndex < 0)
                startIndex = text.IndexOf("SIA-", StringComparison.OrdinalIgnoreCase);

            var payload = startIndex >= 0
                ? text[startIndex..]
                : text.Contains('\r') ? text[..(text.IndexOf('\r') + 1)] : text;

            if (!string.IsNullOrEmpty(payload))
            {
                var last = payload[payload.Length - 1];
                if (last == '\r' || last == '\n' || last == 0x03)
                {
                    message.Terminator = last.ToString();
                    payload = payload[..^1];
                }
            }

            message.Payload = payload;

            var quoteIndex = payload.IndexOf('"');
            if (quoteIndex > 0)
            {
                message.ProtocolId = payload[..quoteIndex];
                var idMatch = IdLinkRegex.Match(payload);
                if (idMatch.Success)
                {
                    message.IdSpravy = idMatch.Groups[1].Value;
                    if (idMatch.Groups[3].Success)
                        message.LinkNumber = idMatch.Groups[3].Value;
                }
            }

            var parts = payload.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
            var rest = parts.Length > 1 ? parts[1] : string.Empty;

            var timeMatch = TimeRegex.Match(rest);
            if (timeMatch.Success)
                message.Time = timeMatch.Groups[1].Value;

            var dateMatch = DateRegex.Match(rest);
            if (dateMatch.Success)
                message.Date = dateMatch.Groups[1].Value;

            var accountMatch = AccountRegex.Match(payload);
            if (accountMatch.Success)
                message.AccountNumber = accountMatch.Groups[1].Value;

            var firstBracketMatch = FirstBracketRegex.Match(payload);
            var firstBlock = firstBracketMatch.Success ? firstBracketMatch.Groups[1].Value : string.Empty;

            var firstBlockMatch = FirstBlockRegex.Match(firstBlock);
            if (firstBlockMatch.Success)
            {
                message.AccountNumber = firstBlockMatch.Groups[1].Value;
                message.NriId = firstBlockMatch.Groups[2].Value;
                message.Zone = firstBlockMatch.Groups[3].Value;
                var maybeEvent = firstBlockMatch.Groups[4].Value;
                message.User = firstBlockMatch.Groups[5].Value;

                if (!string.IsNullOrWhiteSpace(maybeEvent) && maybeEvent.StartsWith("/", StringComparison.Ordinal))
                {
                    maybeEvent = maybeEvent[1..];
                    message.EventCode = maybeEvent;
                    message.EventCodeKey = new string(maybeEvent.Where(c => !char.IsDigit(c)).ToArray());
                    message.EventDescriptions = SiaEventCodeCatalog.FindDescriptions(message.EventCodeKey);
                }
            }

            var allBrackets = new List<string>();
            var allMatches = BracketRegex.Matches(payload);
            foreach (Match match in allMatches)
            {
                if (match.Success)
                    allBrackets.Add(match.Groups[1].Value);
            }

            if (allBrackets.Count >= 2)
            {
                var secondBlock = allBrackets[1];
                if (!string.IsNullOrEmpty(secondBlock))
                {
                    message.TypeMessage = secondBlock[0].ToString();
                    message.ExtraMessage = secondBlock.Length > 1 ? secondBlock[1..] : string.Empty;
                }
            }

            return message;
        }

        private static byte[] HexToBytes(string input)
        {
            var chars = input.Where(Uri.IsHexDigit).ToArray();
            if (chars.Length == 0)
                return Array.Empty<byte>();

            if (chars.Length % 2 != 0)
                chars = chars[..^1];

            var result = new byte[chars.Length / 2];
            for (var i = 0; i < chars.Length; i += 2)
            {
                result[i / 2] = Convert.ToByte(new string(new[] { chars[i], chars[i + 1] }), 16);
            }

            return result;
        }

        private static string DecodeWithFallback(byte[] data)
        {
            if (data == null || data.Length == 0)
                return string.Empty;

            var utf8 = Encoding.UTF8.GetString(data);
            if (!utf8.Contains('\uFFFD'))
                return utf8;

            try
            {
                return Encoding.GetEncoding("windows-1250").GetString(data);
            }
            catch
            {
            }

            try
            {
                return Encoding.GetEncoding("iso-8859-2").GetString(data);
            }
            catch
            {
            }

            var fallback = new StringBuilder(data.Length);
            foreach (var b in data)
            {
                var c = b;
                fallback.Append(c >= 0x20 && c <= 0x7E ? (char)c : '.');
            }

            return fallback.ToString();
        }
    }
}
