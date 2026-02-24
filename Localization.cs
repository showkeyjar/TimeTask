using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Threading;
using System.Windows.Data;
using System.Windows.Markup;

namespace TimeTask
{
    public static class I18n
    {
        private static readonly ResourceManager ResourceManager =
            new ResourceManager("TimeTask.Properties.Resources", typeof(Properties.Resources).Assembly);

        private static CultureInfo _currentCulture = CultureInfo.GetCultureInfo("zh-CN");

        public static event EventHandler LanguageChanged;

        public static CultureInfo CurrentCulture => _currentCulture;

        public static void InitializeFromSettings()
        {
            string language = Properties.Settings.Default.UiLanguage;
            if (string.IsNullOrWhiteSpace(language))
            {
                language = "zh-CN";
            }

            SetLanguage(language, persist: false);
        }

        public static void SetLanguage(string languageCode, bool persist = true)
        {
            CultureInfo culture;
            try
            {
                culture = CultureInfo.GetCultureInfo(languageCode);
            }
            catch (CultureNotFoundException)
            {
                culture = CultureInfo.GetCultureInfo("zh-CN");
            }

            bool changed = !_currentCulture.Name.Equals(culture.Name, StringComparison.OrdinalIgnoreCase);
            _currentCulture = culture;
            Properties.Resources.Culture = culture;
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            if (persist)
            {
                SaveLanguage(culture.Name);
            }

            if (changed)
            {
                LanguageChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static string T(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return string.Empty;
            }

            string value = ResourceManager.GetString(key, _currentCulture);
            if (string.IsNullOrWhiteSpace(value))
            {
                return key;
            }

            return value;
        }

        public static string Tf(string key, params object[] args)
        {
            string format = T(key);
            if (args == null || args.Length == 0)
            {
                return format;
            }

            return string.Format(_currentCulture, format, args);
        }

        private static void SaveLanguage(string languageCode)
        {
            try
            {
                Properties.Settings.Default.UiLanguage = languageCode;
                Properties.Settings.Default.Save();
            }
            catch
            {
                // Ignore persistence issues to avoid blocking UI.
            }
        }
    }

    [MarkupExtensionReturnType(typeof(string))]
    public class LocExtension : MarkupExtension, INotifyPropertyChanged
    {
        private static bool _subscribed;

        public string Key { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;

        public LocExtension()
        {
            EnsureSubscribed();
            GlobalLanguageChanged += HandleLanguageChanged;
        }

        public string Value => I18n.T(Key);

        public override object ProvideValue(IServiceProvider serviceProvider)
        {
            var binding = new Binding(nameof(Value))
            {
                Source = this,
                Mode = BindingMode.OneWay
            };
            return binding.ProvideValue(serviceProvider);
        }

        private static event EventHandler GlobalLanguageChanged;

        private static void EnsureSubscribed()
        {
            if (_subscribed)
            {
                return;
            }

            I18n.LanguageChanged += (_, __) => GlobalLanguageChanged?.Invoke(null, EventArgs.Empty);
            _subscribed = true;
        }

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public LocExtension(string key) : this()
        {
            Key = key;
        }

        private void HandleLanguageChanged(object sender, EventArgs e)
        {
            OnPropertyChanged(nameof(Value));
        }
    }
}
