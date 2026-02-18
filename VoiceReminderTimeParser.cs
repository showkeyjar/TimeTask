using System;
using System.Globalization;
using System.Text.RegularExpressions;

namespace TimeTask
{
    /// <summary>
    /// 从中文口语中解析提醒时间，支持常见“今天/明天/周X/几点/几分/XX后”表达。
    /// </summary>
    public sealed class VoiceReminderTimeParser
    {
        private static readonly Regex RelativeDelayRegex =
            new Regex(@"(?<num>\d+)\s*(?<unit>分钟|分|小时|个小时|天)\s*后", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex HalfHourDelayRegex =
            new Regex(@"半小时后", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex TimeRegex =
            new Regex(@"(?<hour>[0-2]?\d)\s*(点|:|：)\s*(?<min>[0-5]?\d)?\s*(分)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex MonthDayRegex =
            new Regex(@"(?<month>1[0-2]|0?[1-9])\s*月\s*(?<day>3[01]|[12]?\d)\s*(日|号)?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex DayOnlyRegex =
            new Regex(@"(?<day>3[01]|[12]?\d)\s*(日|号)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private static readonly Regex WeekdayRegex =
            new Regex(@"(周|星期)(?<day>[一二三四五六日天])", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public bool TryParse(string text, DateTime now, out DateTime reminderTime)
        {
            reminderTime = DateTime.MinValue;
            if (string.IsNullOrWhiteSpace(text))
                return false;

            string normalized = text.Trim();

            if (TryParseRelativeDelay(normalized, now, out reminderTime))
                return true;

            bool hasToday = normalized.Contains("今天");
            bool hasTomorrow = normalized.Contains("明天");
            bool hasAfterTomorrow = normalized.Contains("后天");
            bool hasTonight = normalized.Contains("今晚") || normalized.Contains("今晚上");
            bool hasTomorrowMorning = normalized.Contains("明早") || normalized.Contains("明天早");
            bool hasTomorrowEvening = normalized.Contains("明晚") || normalized.Contains("明天晚");

            DateTime date = now.Date;
            if (hasTomorrow) date = now.Date.AddDays(1);
            if (hasAfterTomorrow) date = now.Date.AddDays(2);

            if (TryParseWeekday(normalized, now, out DateTime weekDate))
                date = weekDate.Date;

            if (TryParseMonthDay(normalized, now, out DateTime monthDate))
                date = monthDate.Date;
            else if (TryParseDayOnly(normalized, now, out DateTime dayOnlyDate))
                date = dayOnlyDate.Date;

            int hour = -1;
            int minute = 0;
            if (TryParseClockTime(normalized, out hour, out minute))
            {
                hour = NormalizeHourByPartOfDay(normalized, hour);
            }
            else if (hasTonight || hasTomorrowEvening || normalized.Contains("晚上"))
            {
                hour = 20;
            }
            else if (hasTomorrowMorning || normalized.Contains("早上") || normalized.Contains("上午"))
            {
                hour = 9;
            }
            else if (normalized.Contains("中午"))
            {
                hour = 12;
            }
            else if (hasToday || hasTomorrow || hasAfterTomorrow || normalized.Contains("周") || normalized.Contains("星期"))
            {
                hour = 9;
            }

            if (hour < 0)
                return false;

            reminderTime = new DateTime(date.Year, date.Month, date.Day, hour, minute, 0, DateTimeKind.Local);
            if (reminderTime < now.AddMinutes(1))
            {
                if (hasToday)
                {
                    reminderTime = reminderTime.AddDays(1);
                }
            }
            return true;
        }

        private static bool TryParseRelativeDelay(string text, DateTime now, out DateTime reminderTime)
        {
            reminderTime = DateTime.MinValue;
            if (HalfHourDelayRegex.IsMatch(text))
            {
                reminderTime = now.AddMinutes(30);
                return true;
            }

            var match = RelativeDelayRegex.Match(text);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["num"].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int amount))
                return false;

            string unit = match.Groups["unit"].Value;
            if (unit.Contains("天"))
                reminderTime = now.AddDays(amount);
            else if (unit.Contains("小时"))
                reminderTime = now.AddHours(amount);
            else
                reminderTime = now.AddMinutes(amount);

            return true;
        }

        private static bool TryParseClockTime(string text, out int hour, out int minute)
        {
            hour = -1;
            minute = 0;
            var match = TimeRegex.Match(text);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["hour"].Value, out hour))
                return false;

            if (match.Groups["min"].Success && !string.IsNullOrWhiteSpace(match.Groups["min"].Value))
            {
                int.TryParse(match.Groups["min"].Value, out minute);
            }

            if (hour < 0 || hour > 23 || minute < 0 || minute > 59)
                return false;

            return true;
        }

        private static int NormalizeHourByPartOfDay(string text, int hour)
        {
            bool hasAfternoon = text.Contains("下午");
            bool hasEvening = text.Contains("晚上") || text.Contains("今晚") || text.Contains("明晚");
            bool hasMorning = text.Contains("早上") || text.Contains("上午") || text.Contains("明早");

            if ((hasAfternoon || hasEvening) && hour < 12)
            {
                return Math.Min(23, hour + 12);
            }

            if (hasMorning && hour == 12)
            {
                return 0;
            }

            return hour;
        }

        private static bool TryParseWeekday(string text, DateTime now, out DateTime date)
        {
            date = now.Date;
            var match = WeekdayRegex.Match(text);
            if (!match.Success)
                return false;

            int target = ChineseWeekdayToInt(match.Groups["day"].Value);
            if (target < 0)
                return false;

            int current = (int)now.DayOfWeek;
            int delta = (target - current + 7) % 7;
            if (delta == 0)
                delta = 7;
            date = now.Date.AddDays(delta);
            return true;
        }

        private static int ChineseWeekdayToInt(string day)
        {
            switch (day)
            {
                case "日":
                case "天":
                    return 0;
                case "一":
                    return 1;
                case "二":
                    return 2;
                case "三":
                    return 3;
                case "四":
                    return 4;
                case "五":
                    return 5;
                case "六":
                    return 6;
                default:
                    return -1;
            }
        }

        private static bool TryParseMonthDay(string text, DateTime now, out DateTime date)
        {
            date = now.Date;
            var match = MonthDayRegex.Match(text);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["month"].Value, out int month) ||
                !int.TryParse(match.Groups["day"].Value, out int day))
                return false;

            int year = now.Year;
            try
            {
                var parsed = new DateTime(year, month, day);
                if (parsed.Date < now.Date)
                    parsed = parsed.AddYears(1);
                date = parsed.Date;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryParseDayOnly(string text, DateTime now, out DateTime date)
        {
            date = now.Date;
            var match = DayOnlyRegex.Match(text);
            if (!match.Success)
                return false;

            if (!int.TryParse(match.Groups["day"].Value, out int day))
                return false;

            try
            {
                var parsed = new DateTime(now.Year, now.Month, day);
                if (parsed.Date < now.Date)
                {
                    var next = now.AddMonths(1);
                    parsed = new DateTime(next.Year, next.Month, Math.Min(day, DateTime.DaysInMonth(next.Year, next.Month)));
                }
                date = parsed.Date;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
