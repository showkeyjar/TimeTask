using System;
using System.Configuration;
using System.Windows;
using System.ComponentModel;

namespace TimeTask.Properties
{
    public partial class Settings
    {
        [global::System.Configuration.UserScopedSetting()]
        [global::System.Diagnostics.DebuggerNonUserCode()]
        [global::System.Configuration.DefaultSettingValue("Normal")]
        public global::System.Windows.WindowState WindowState
        {
            get
            {
                try
                {
                    return (global::System.Windows.WindowState)this["WindowState"];
                }
                catch
                {
                    return global::System.Windows.WindowState.Normal;
                }
            }
            set
            {
                this["WindowState"] = value;
            }
        }
    }
}
