using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace WhiteSpace.Pages
{
    public partial class BoardPage : Page
    {
        private Guid _boardId;

        public BoardPage(Guid boardId)
        {
            InitializeComponent();
            _boardId = boardId;

            // Загрузим данные доски по ID (например, название доски или другие элементы)
            LoadBoardData();
        }

        private void LoadBoardData()
        {
            // Здесь можно загрузить информацию о доске из базы данных с помощью SupabaseService
            // Например, показать название доски или другие детали
        }

        // Методы для рисования
        private void Pen_Click(object sender, RoutedEventArgs e)
        {
            // Логика для рисования на Canvas
        }

        private void Rect_Click(object sender, RoutedEventArgs e)
        {
            // Логика для рисования прямоугольника
        }

        private void Ellipse_Click(object sender, RoutedEventArgs e)
        {
            // Логика для рисования круга
        }

        private void Text_Click(object sender, RoutedEventArgs e)
        {
            // Логика для добавления текста на Canvas
        }
    }
}
