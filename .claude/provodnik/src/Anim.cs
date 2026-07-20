// Мелкая анимация на таймере: плавные переходы цвета и сдвиги.
// Отдельная библиотека ради этого не нужна — движений в программе немного.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Provodnik
{
    static class Anim
    {
        class Job
        {
            public int Elapsed;
            public int Duration;
            public Action<double> Step;
            public Action Done;
        }

        static readonly List<Job> jobs = new List<Job>();
        static Timer timer;

        const int Tick = 15;                    // ~60 кадров в секунду

        /// <summary>Запускает анимацию: Step получает долю пути от 0 до 1 (уже сглаженную).</summary>
        public static void Run(int durationMs, Action<double> step, Action done)
        {
            if (step == null) return;
            step(0);
            Job job = new Job();
            job.Duration = Math.Max(Tick, durationMs);
            job.Step = step;
            job.Done = done;
            jobs.Add(job);

            if (timer == null)
            {
                timer = new Timer();
                timer.Interval = Tick;
                timer.Tick += OnTick;
            }
            timer.Start();
        }

        public static void Run(int durationMs, Action<double> step)
        {
            Run(durationMs, step, null);
        }

        static void OnTick(object sender, EventArgs e)
        {
            for (int i = jobs.Count - 1; i >= 0; i--)
            {
                Job job = jobs[i];
                job.Elapsed += Tick;
                double t = Math.Min(1.0, (double)job.Elapsed / job.Duration);
                try { job.Step(Ease(t)); }
                catch (Exception) { t = 1.0; }
                if (t >= 1.0)
                {
                    jobs.RemoveAt(i);
                    if (job.Done != null)
                    {
                        try { job.Done(); } catch (Exception) { }
                    }
                }
            }
            if (jobs.Count == 0) timer.Stop();
        }

        /// <summary>Замедление к концу — движение выглядит живым, а не механическим.</summary>
        static double Ease(double t)
        {
            return 1 - Math.Pow(1 - t, 3);
        }

        /// <summary>Промежуточный цвет между двумя.</summary>
        public static Color Mix(Color from, Color to, double t)
        {
            if (t < 0) t = 0;
            if (t > 1) t = 1;
            return Color.FromArgb(
                (int)(from.R + (to.R - from.R) * t),
                (int)(from.G + (to.G - from.G) * t),
                (int)(from.B + (to.B - from.B) * t));
        }

        /// <summary>Плавно перекрашивает фон элемента.</summary>
        public static void ColorTo(Control control, Color target, int durationMs)
        {
            Color from = control.BackColor;
            if (from == target) return;
            Run(durationMs, delegate (double t) { control.BackColor = Mix(from, target, t); });
        }
    }
}
