using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
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
                let parse = int.TryParse(temparry[1], out parseScore)
                let isSkip = temparry.Length > 2 && temparry[3] != null && temparry[3] == "True"
                select new ItemGrid { Task = temparry[0], Score = parseScore, Result=temparry[2], Done = !isSkip };
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

        string currentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

        int task1_selected_indexs = -1;
        int task2_selected_indexs = -1;
        int task3_selected_indexs = -1;
        int task4_selected_indexs = -1;

        public void loadDataGridView()
        {
            task1.ItemsSource = HelperClass.ReadCsv(currentPath + "/data/1.csv");
            task1.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
            task2.ItemsSource = HelperClass.ReadCsv(currentPath + "/data/2.csv");
            task2.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
            task3.ItemsSource = HelperClass.ReadCsv(currentPath + "/data/3.csv");
            task3.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
            task4.ItemsSource = HelperClass.ReadCsv(currentPath + "/data/4.csv");
            task4.Items.SortDescriptions.Add(new SortDescription("Score", ListSortDirection.Descending));
        }

        public MainWindow()
        {
            InitializeComponent();
            this.Top = (double)Properties.Settings.Default.Top;
            this.Left = (double)Properties.Settings.Default.Left;
            loadDataGridView();
        }

        private void update_csv(DataGrid dgv, string number) {
            var temp = new List<ItemGrid>();
            for (int i = 0; i < dgv.Items.Count; i++)
            {
                if (dgv.Items[i] is ItemGrid)
                    temp.Add((ItemGrid)dgv.Items[i]);
            }
            HelperClass.WriteCsv(temp, currentPath + "/data/" + number + ".csv");
        }

        private void task1_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task1_selected_indexs = task1.SelectedIndex;
            update_csv(task1, "1");
        }

        private void task2_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task2_selected_indexs = task2.SelectedIndex;
            update_csv(task2, "2");
        }

        private void task3_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task3_selected_indexs = task3.SelectedIndex;
            update_csv(task3, "3");
        }

        private void task4_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            task4_selected_indexs = task4.SelectedIndex;
            update_csv(task4, "4");
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

        private void del1_Click(object sender, RoutedEventArgs e)
        {
            if (task1_selected_indexs >= 0)
            {
                var itemList = (List<ItemGrid>)task1.ItemsSource;
                itemList.RemoveAt(task1_selected_indexs);
                task1.ItemsSource = itemList;
            }
        }

        private void del2_Click(object sender, RoutedEventArgs e)
        {
            DataRowView selectedItem = task2.SelectedItem as DataRowView;
            if (selectedItem != null)
            {
                DataView dataView = task2.ItemsSource as DataView;
                dataView.Table.Rows.Remove(selectedItem.Row);
            }
        }

        private void del3_Click(object sender, RoutedEventArgs e)
        {
            DataRowView selectedItem = task3.SelectedItem as DataRowView;
            if (selectedItem != null)
            {
                DataView dataView = task3.ItemsSource as DataView;
                dataView.Table.Rows.Remove(selectedItem.Row);
            }
        }

        private void del4_Click(object sender, RoutedEventArgs e)
        {
            DataRowView selectedItem = task4.SelectedItem as DataRowView;
            if (selectedItem != null)
            {
                DataView dataView = task4.ItemsSource as DataView;
                dataView.Table.Rows.Remove(selectedItem.Row);
            }
        }
    }
}
