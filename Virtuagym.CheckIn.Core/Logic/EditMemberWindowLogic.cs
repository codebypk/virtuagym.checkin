using System;
using System.Globalization;
using System.Net.Mail;
using System.Text.RegularExpressions;
using Virtuagym.API.v1.Models;

namespace Virtuagym.CheckIn.Core.Logic
{
    public static class EditMemberWindowLogic
    {
        private static readonly Regex PhonePattern = new Regex(@"^[0-9+\-/() .]*$", RegexOptions.Compiled);
        private static readonly Regex ZipPattern = new Regex(@"^[A-Za-z0-9ÄÖÜäöüß\- ]*$", RegexOptions.Compiled);
        private static readonly Regex LanguagePattern = new Regex(@"^[A-Za-z]{2,8}(-[A-Za-z]{2,8})?$", RegexOptions.Compiled);

        public static MemberResult CreateSanitizedMember(MemberResult member)
        {
            if (member == null)
                return null;

            return new MemberResult
            {
                firstname = Normalize(member.firstname),
                lastname = Normalize(member.lastname),
                email = Normalize(member.email),
                gender = Normalize(member.gender).ToLowerInvariant(),
                birthday = NormalizeDateForComparison(member.birthday),
                lang = Normalize(member.lang).ToLowerInvariant(),
                phone = Normalize(member.phone),
                mobile = Normalize(member.mobile),
                street = Normalize(member.street),
                street_extra = Normalize(member.street_extra),
                zip = Normalize(member.zip),
                place = Normalize(member.place),
                rfid_tag = Normalize(member.rfid_tag),
                external_id = Normalize(member.external_id),
                is_pro = member.is_pro,
                active = member.active
            };
        }

        public static bool AreEquivalent(MemberResult left, MemberResult right)
        {
            var a = CreateSanitizedMember(left);
            var b = CreateSanitizedMember(right);

            if (a == null || b == null)
                return a == b;

            return string.Equals(a.firstname, b.firstname, StringComparison.Ordinal)
                && string.Equals(a.lastname, b.lastname, StringComparison.Ordinal)
                && string.Equals(a.email, b.email, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.gender, b.gender, StringComparison.Ordinal)
                && string.Equals(a.birthday, b.birthday, StringComparison.Ordinal)
                && string.Equals(a.lang, b.lang, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.phone, b.phone, StringComparison.Ordinal)
                && string.Equals(a.mobile, b.mobile, StringComparison.Ordinal)
                && string.Equals(a.street, b.street, StringComparison.Ordinal)
                && string.Equals(a.street_extra, b.street_extra, StringComparison.Ordinal)
                && string.Equals(a.zip, b.zip, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.place, b.place, StringComparison.Ordinal)
                && string.Equals(a.rfid_tag, b.rfid_tag, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.external_id, b.external_id, StringComparison.Ordinal)
                && a.is_pro == b.is_pro
                && a.active == b.active;
        }

        public static bool ValidateMemberUpdate(MemberResult member, out string errorField, out string errorMessage)
        {
            errorField = null;
            errorMessage = null;

            if (member == null)
            {
                errorField = "General";
                errorMessage = "Member darf nicht null sein.";
                return false;
            }

            if (!IsOptionalEmailValid(member.email))
            {
                errorField = "Email";
                errorMessage = "Invalid email.";
                return false;
            }

            if (!TryNormalizeOptionalDate(member.birthday, out _))
            {
                errorField = "Birthday";
                errorMessage = "Invalid birthday.";
                return false;
            }

            if (!IsOptionalPhoneValid(member.phone))
            {
                errorField = "Phone";
                errorMessage = "Invalid phone.";
                return false;
            }

            if (!IsOptionalPhoneValid(member.mobile))
            {
                errorField = "Mobile";
                errorMessage = "Invalid mobile.";
                return false;
            }

            if (!IsOptionalZipValid(member.zip))
            {
                errorField = "Zip";
                errorMessage = "Invalid zip.";
                return false;
            }

            if (!IsOptionalLanguageValid(member.lang))
            {
                errorField = "Language";
                errorMessage = "Invalid language.";
                return false;
            }

            return true;
        }

        public static bool TryValidateCreditAssignment(
            string amountText,
            bool unlimited,
            string serviceType,
            string validUntil,
            out int amount,
            out string normalizedServiceType,
            out string normalizedValidUntil,
            out string errorField,
            out string errorMessage)
        {
            amount = 0;
            normalizedServiceType = Normalize(serviceType);
            normalizedValidUntil = null;
            errorField = null;
            errorMessage = null;

            if (string.IsNullOrWhiteSpace(normalizedServiceType))
            {
                errorField = "CreditServiceType";
                errorMessage = "Missing service type.";
                return false;
            }

            if (!TryNormalizeOptionalDate(validUntil, out normalizedValidUntil))
            {
                errorField = "CreditValidUntil";
                errorMessage = "Invalid valid-until date.";
                return false;
            }

            if (unlimited)
                return true;

            if (!int.TryParse(Normalize(amountText), NumberStyles.Integer, CultureInfo.InvariantCulture, out amount) || amount <= 0)
            {
                errorField = "CreditAmount";
                errorMessage = "Invalid amount.";
                return false;
            }

            return true;
        }

        public static bool TryNormalizeOptionalDate(string value, out string normalized)
        {
            normalized = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                normalized = string.Empty;
                return true;
            }

            DateTime parsed;
            if (DateTime.TryParseExact(normalized, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed)
                || DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.None, out parsed)
                || DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            {
                normalized = parsed.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                return true;
            }

            return false;
        }

        public static bool IsOptionalEmailValid(string value)
        {
            value = Normalize(value);
            if (string.IsNullOrWhiteSpace(value))
                return true;

            try
            {
                var address = new MailAddress(value);
                return string.Equals(address.Address, value, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsOptionalPhoneValid(string value)
        {
            value = Normalize(value);
            return string.IsNullOrWhiteSpace(value) || PhonePattern.IsMatch(value);
        }

        public static bool IsOptionalZipValid(string value)
        {
            value = Normalize(value);
            return string.IsNullOrWhiteSpace(value) || ZipPattern.IsMatch(value);
        }

        public static bool IsOptionalLanguageValid(string value)
        {
            value = Normalize(value);
            return string.IsNullOrWhiteSpace(value) || LanguagePattern.IsMatch(value);
        }

        private static string Normalize(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static string NormalizeDateForComparison(string value)
        {
            return TryNormalizeOptionalDate(value, out string normalized) ? normalized : Normalize(value);
        }
    }
}
