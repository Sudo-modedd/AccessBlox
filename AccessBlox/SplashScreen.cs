using System;
using System.Drawing;
using System.Windows.Forms;

namespace AccessBlox
{
    public partial class SplashScreen : Form
    {
        public SplashScreen()
        {
            InitializeComponent();

            // Настройка формы
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.ShowInTaskbar = false;// Увеличение размера формы
        }

        // 1. Метод для обновления текстового статуса
        public void SetStatus(string message)
        {
            // !!! ВАЖНО: Убедитесь, что ваш Label для статуса называется statusLabel !!!
            if (statusLabel.InvokeRequired)
            {
                statusLabel.Invoke(new Action(() => statusLabel.Text = message));
            }
            else
            {
                statusLabel.Text = message;
            }
            this.Refresh();
        }

        // 2. Метод для обновления прогресс-бара
        public void SetProgress(int value)
        {
            // !!! ВАЖНО: Убедитесь, что ваш ProgressBar называется guna2ProgressBar1 !!!
            if (guna2ProgressBar1.InvokeRequired)
            {
                guna2ProgressBar1.Invoke(new Action(() => guna2ProgressBar1.Value = value));
            }
            else
            {
                guna2ProgressBar1.Value = value;
            }
            this.Refresh();
        }

        private void SplashScreen_Load(object sender, EventArgs e)
        {

        }
    }
}