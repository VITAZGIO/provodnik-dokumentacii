// Настройка списка подпапок заявки — по одной в строке.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Provodnik
{
    class SubfoldersDialog : Form
    {
        readonly TextBox text;

        /// <summary>Что человек ввёл — уже очищенное и без повторов.</summary>
        public List<string> Result { get; private set; }

        public SubfoldersDialog(List<string> current)
        {
            Text = "Подпапки заявки";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ClientSize = new Size(420, 300);
            BackColor = Theme.Panel;
            Font = Theme.Sans;

            Label hint = new Label();
            hint.Text = "По одной в строке. Применяется к новым и открываемым заявкам.";
            hint.ForeColor = Theme.InkSoft;
            hint.SetBounds(14, 12, 400, 18);
            Controls.Add(hint);

            text = new TextBox();
            text.Multiline = true;
            text.ScrollBars = ScrollBars.Vertical;
            text.Font = Theme.Mono;
            text.SetBounds(14, 36, 392, 208);
            text.Text = string.Join("\r\n", current.ToArray());
            Controls.Add(text);

            Button ok = new Button();
            ok.Text = "Сохранить";
            ok.BackColor = Theme.Tag;
            ok.ForeColor = Theme.TagInk;
            ok.FlatStyle = FlatStyle.Flat;
            ok.Font = Theme.SansBold;
            ok.SetBounds(296, 256, 110, 30);
            ok.Click += OnSave;
            Controls.Add(ok);

            Button cancel = new Button();
            cancel.Text = "Отмена";
            cancel.SetBounds(180, 256, 110, 30);
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        void OnSave(object sender, EventArgs e)
        {
            List<string> subs = new List<string>();
            foreach (string line in text.Lines)
            {
                string s = Util.Sanitize(line);
                if (s.Length > 0 && !subs.Contains(s)) subs.Add(s);
            }
            if (subs.Count == 0)
            {
                MessageBox.Show(this, "Список пуст — нужна хотя бы одна подпапка.",
                                "Проводник", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Result = subs;
            DialogResult = DialogResult.OK;
        }
    }
}
