using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;

namespace TimeTask
{

    public static class HelperClass
    {

        public static List<ItemGrid> ReadCsv(string filepath)
        {
            if (!File.Exists(filepath))
            {
                return null;
            }
            int parseScore = 0;
            var allLines = File.ReadAllLines(filepath).Where(arg => !string.IsNullOrWhiteSpace(arg));
            var result =
                from line in allLines.Skip(1).Take(allLines.Count() - 1)
                let temparry = line.Split(',')
                let isSkip = temparry.Length > 2 && temparry[2] != null && temparry[2] == "True"
                let parse = int.TryParse(temparry[1],out parseScore)
                select new ItemGrid { Task = temparry[0], Score = parseScore, Done = !isSkip };
            var result_list = new List<ItemGrid>();
            try
            {
                result_list = result.ToList();
            }
            catch {
                result_list.Add(new ItemGrid { Task = "csv文件缺失", Score = parseScore, Result= "", Done = false });
            }
            return result_list;
        }

        public static void WriteCsv(IEnumerable<ItemGrid> items, string filepath)
        {
            var temparray = items.Select(item => item.Task + "," + item.Score + "," + item.Result + "," + (item.Done ? "False" : "True")).ToArray();
            var contents = new string[temparray.Length + 2];
            Array.Copy(temparray, 0, contents, 1, temparray.Length);
            contents[0] = "task,score,result,done";
            File.WriteAllLines(filepath, contents);
        }
    }

    public class ItemGrid
    {
        public string Task { set; get; }
        public int Score { set; get; }
        public string Result { set; get; }
        public bool Done { set; get; }

    }

    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {

        [DllImport("user32.dll", SetLastError = true)]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpWindowClass, string lpWindowName);
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr parentHandle, IntPtr childAfter, string className, string windowTitle);
        const int GWL_HWNDPARENT = -8;
        [DllImport("user32.dll")]
        static extern IntPtr SetParent(IntPtr hWndChild, IntPtr hWndNewParent);

        public void loadDataGridView()
        {
            task1.ItemsSource = HelperClass.ReadCsv(@"data/1.csv");
            task2.ItemsSource = HelperClass.ReadCsv(@"data/2.csv");
            task3.ItemsSource = HelperClass.ReadCsv(@"data/3.csv");
            task4.ItemsSource = HelperClass.ReadCsv(@"data/4.csv");
        }

        public MainWindow()
        {
            InitializeComponent();
            this.Top = (double)Properties.Settings.Default.Top;
            this.Left = (double)Properties.Settings.Default.Left;
            loadDataGridView();
        }

        private void task1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var temp = new List<ItemGrid>();
            for (int i = 0; i < task1.Items.Count; i++)
            {
                if (task1.Items[i] is ItemGrid)
                    temp.Add((ItemGrid)task1.Items[i]);
            }
            HelperClass.WriteCsv(temp, @"data/1.csv");
        }

        private void task2_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var temp = new List<ItemGrid>();
            for (int i = 0; i < task2.Items.Count; i++)
            {
                if (task2.Items[i] is ItemGrid)
                    temp.Add((ItemGrid)task2.Items[i]);
            }
            HelperClass.WriteCsv(temp, @"data/2.csv");
        }

        private void task3_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var temp = new List<ItemGrid>();
            for (int i = 0; i < task3.Items.Count; i++)
            {
                if (task3.Items[i] is ItemGrid)
                    temp.Add((ItemGrid)task3.Items[i]);
            }
            HelperClass.WriteCsv(temp, @"data/3.csv");
        }

        private void task4_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var temp = new List<ItemGrid>();
            for (int i = 0; i < task4.Items.Count; i++)
            {
                if (task4.Items[i] is ItemGrid)
                    temp.Add((ItemGrid)task4.Items[i]);
            }
            HelperClass.WriteCsv(temp, @"data/4.csv");
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            this.DragMove();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            IntPtr pWnd = FindWindow("Progman", null);
            pWnd = FindWindowEx(pWnd, IntPtr.Zero, "SHELLDLL_DefVIew", null);
            pWnd = FindWindowEx(pWnd, IntPtr.Zero, "SysListView32", null);
            IntPtr tWnd = new WindowInteropHelper(this).Handle;
            SetParent(tWnd, pWnd);
        }

        private void location_Save(object sender, EventArgs e)
        {
            Properties.Settings.Default.Top = this.Top;
            Properties.Settings.Default.Left = this.Left;
            Properties.Settings.Default.Save();
        }
    }
}
