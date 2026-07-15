// Окно-предупреждение перед включением публичного доступа.
// Чтобы включить, нужно вручную набрать слово ПОДТВЕРДИТЬ — случайно не нажмёшь.
using System;
using System.Drawing;
using System.Windows.Forms;

namespace Provodnik
{
    class ConfirmDialog : Form
    {
        const string Word = "ПОДТВЕРДИТЬ";

        readonly TextBox input;
        readonly Button okButton;

        public ConfirmDialog()
        {
            Text = "Публичный доступ через интернет";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(520, 330);
            BackColor = Theme.Panel;
            Font = Theme.Sans;

            Label warn = new Label();
            warn.Text = "ВНИМАНИЕ";
            warn.ForeColor = Theme.Err;
            warn.Font = new Font(Theme.Mono.FontFamily, 11f, FontStyle.Bold);
            warn.SetBounds(18, 16, 200, 22);
            Controls.Add(warn);

            Label text = new Label();
            text.Text =
                "Сейчас страница отправки фото станет доступна из интернета — по временному " +
                "адресу вида https://случайные-слова.trycloudflare.com\n\n" +
                "• Открыть её сможет любой, кто узнает адрес и код доступа из QR-кода. " +
                "Не показывайте этот QR посторонним и не выкладывайте его никуда.\n\n" +
                "• Файлы пойдут через серверы Cloudflare, а не напрямую по вашей сети.\n\n" +
                "• Размер одного файла — не больше 100 МБ (ограничение Cloudflare).\n\n" +
                "• Доступ выключится сам через 1 час.\n\n" +
                "Включайте только если телефон и компьютер в разных сетях и обычный " +
                "(локальный) QR-код не открывается.";
            text.SetBounds(18, 44, 484, 190);
            Controls.Add(text);

            Label ask = new Label();
            ask.Text = "Чтобы включить, наберите слово  " + Word;
            ask.SetBounds(18, 240, 300, 20);
            Controls.Add(ask);

            input = new TextBox();
            input.Font = Theme.Mono;
            input.SetBounds(18, 262, 240, 26);
            input.TextChanged += delegate { okButton.Enabled = input.Text.Trim() == Word; };
            Controls.Add(input);

            okButton = new Button();
            okButton.Text = "Включить";
            okButton.SetBounds(270, 261, 110, 28);
            okButton.Enabled = false;
            okButton.DialogResult = DialogResult.OK;
            Controls.Add(okButton);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.SetBounds(390, 261, 110, 28);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = okButton;
            CancelButton = cancel;
        }
    }
}
