using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;

namespace SRM_by_Longtygu
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
        }

        // Cập nhật dòng trạng thái, có hiệu ứng fade nhẹ mỗi lần đổi text
        public void SetStatus(string text)
        {
            StatusText.Text = text;
            var storyboard = (Storyboard)FindResource("TextFadeStoryboard");
            storyboard.Begin(StatusText);
        }

        // Cập nhật thanh tiến trình theo % (0 - 100), có animation mượt thay vì nhảy khựng
        public void SetProgress(double percent)
        {
            var anim = new DoubleAnimation(percent, System.TimeSpan.FromMilliseconds(300))
            {
                EasingFunction = new QuadraticEase()
            };
            LoadingProgressBar.BeginAnimation(ProgressBar.ValueProperty, anim);
            PercentText.Text = $"{(int)percent}%";
        }
    }
}