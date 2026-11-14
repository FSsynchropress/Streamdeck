// Synthetisiert typische Streamer-/Meme-Effekte (Sad Trombone, Bass-Boom, 8-Bit, Zensur-Piep …)
// als eigene Werke – keine Samples aus fremden Quellen. Ergebnis: MP3 (Media Foundation), CC0.
using NAudio.MediaFoundation;
using NAudio.Wave;

const int SR = 44100;
var outDir = args.Length > 0 ? args[0] : "out";
Directory.CreateDirectory(outDir);
MediaFoundationApi.Startup();
var rnd = new Random(4711);

// ---------------------------------------------------------------- Bausteine
float[] Buf(double sec) => new float[(int)(sec * SR)];
double Frac(double x) => x - Math.Floor(x);
double Sine(double ph) => Math.Sin(2 * Math.PI * ph);
double Square(double ph, double duty = 0.5) => Frac(ph) < duty ? 1 : -1;
double Tri(double ph) => 1 - 4 * Math.Abs(Frac(ph) - 0.5);
double Saw(double ph) => 2 * Frac(ph) - 1;
double Mtof(double midi) => 440 * Math.Pow(2, (midi - 69) / 12.0);

// Additiver Blechbläser-Klang; brightness 0…1 öffnet die Obertöne.
double Brass(double ph, double brightness)
{
    double s = 0, k = 1.5 - 1.2 * Math.Clamp(brightness, 0, 1);
    for (int n = 1; n <= 12; n++) s += Math.Sin(2 * Math.PI * n * ph) * Math.Exp(-(n - 1) * k) / Math.Sqrt(n);
    return s * 0.5;
}

Func<double, double> Adsr(double a, double d, double s, double r, double dur) => t =>
    t < a ? t / a :
    t < a + d ? 1 - (1 - s) * (t - a) / d :
    t < dur - r ? s :
    t < dur ? s * (dur - t) / r : 0;

Func<double, double> Decay(double rate, double attack = 0.003) => t => (t < attack ? t / attack : 1) * Math.Exp(-(t - attack) * rate);

// Generischer Oszillator: freq(t), Welle(phase, t), Hüllkurve(t)
void Tone(float[] b, double start, double dur, Func<double, double> freq, Func<double, double, double> wave, Func<double, double> env, double gain = 1)
{
    int i0 = (int)(start * SR), n = (int)(dur * SR);
    double ph = 0;
    for (int i = 0; i < n && i0 + i < b.Length; i++)
    {
        double t = (double)i / SR;
        ph += freq(t) / SR;
        b[i0 + i] += (float)(gain * env(t) * wave(ph, t));
    }
}

void Noise(float[] b, double start, double dur, Func<double, double> env, double gain = 1, double lowpass = 1.0)
{
    int i0 = (int)(start * SR), n = (int)(dur * SR);
    double y = 0;
    for (int i = 0; i < n && i0 + i < b.Length; i++)
    {
        y += lowpass * ((rnd.NextDouble() * 2 - 1) - y);
        b[i0 + i] += (float)(gain * env((double)i / SR) * y);
    }
}

void Saturate(float[] b, double drive)
{
    for (int i = 0; i < b.Length; i++) b[i] = (float)Math.Tanh(b[i] * drive);
}

void LowPass(float[] b, double cutoff)
{
    double a = 1 - Math.Exp(-2 * Math.PI * cutoff / SR), y = 0;
    for (int i = 0; i < b.Length; i++) { y += a * (b[i] - y); b[i] = (float)y; }
}

// Einfacher Hall (Kammfilter), damit Synth-Klänge nicht zu trocken wirken.
void Echo(float[] b, double delaySec, double feedback, double mix)
{
    int d = (int)(delaySec * SR);
    var wet = new float[b.Length];
    for (int i = d; i < b.Length; i++) wet[i] = (float)(b[i - d] + wet[i - d] * feedback);
    for (int i = 0; i < b.Length; i++) b[i] += (float)(wet[i] * mix);
}

void Save(string name, float[] b)
{
    // Stille am Ende kürzen, Ränder weich, Spitze auf −1 dBFS
    int end = b.Length;
    while (end > SR / 10 && Math.Abs(b[end - 1]) < 1e-4) end--;
    Array.Resize(ref b, end);
    float peak = b.Max(Math.Abs);
    if (peak > 0) for (int i = 0; i < b.Length; i++) b[i] *= 0.89f / peak;
    int fade = Math.Min(b.Length / 4, SR / 200);
    for (int i = 0; i < fade; i++) { b[i] *= i / (float)fade; b[^(i + 1)] *= i / (float)fade; }

    var pcm = new byte[b.Length * 2];
    for (int i = 0; i < b.Length; i++)
        BitConverter.TryWriteBytes(pcm.AsSpan(i * 2), (short)Math.Clamp(b[i] * 32767, short.MinValue, short.MaxValue));
    using var src = new RawSourceWaveStream(new MemoryStream(pcm), new WaveFormat(SR, 16, 1));
    var path = Path.Combine(outDir, name + ".mp3");
    MediaFoundationEncoder.EncodeToMp3(src, path, 128000);
    Console.WriteLine($"{name}.mp3  {b.Length / (double)SR:0.00} s");
}

// ---------------------------------------------------------------- Sounds

{   // Sad Trombone: vier chromatisch fallende „Wah“-Töne, der letzte lang mit Vibrato
    var b = Buf(3.4);
    double[] notes = { 58, 57, 56, 55 };
    double[] durs = { 0.42, 0.42, 0.42, 1.5 };
    double t0 = 0.02;
    for (int k = 0; k < 4; k++)
    {
        double dur = durs[k], f = Mtof(notes[k] - 12);
        bool last = k == 3;
        Tone(b, t0, dur,
            t => f * (1 + (last ? 0.035 * Math.Min(1, t / 0.4) * Math.Sin(2 * Math.PI * 5.5 * t) : 0)) * (last ? 1 - 0.03 * t / dur : 1),
            (ph, t) => Brass(ph, Math.Pow(Math.Sin(Math.PI * Math.Min(1, t / (last ? 0.5 : dur))), 0.8) * (last ? 0.9 : 1)),
            Adsr(0.04, 0.1, 0.85, last ? 0.5 : 0.08, dur));
        t0 += dur + 0.04;
    }
    Echo(b, 0.07, 0.3, 0.25);
    Save("sad-trombone", b);
}

{   // Fanfare: Ta-ta-ta-taaa mit Akkord
    var b = Buf(2.2);
    double[] starts = { 0, 0.16, 0.32, 0.5 };
    for (int k = 0; k < 3; k++)
        Tone(b, starts[k], 0.13, _ => Mtof(67), (ph, t) => Brass(ph, 0.9), Adsr(0.01, 0.05, 0.7, 0.04, 0.13));
    foreach (var m in new[] { 72, 76, 79, 60 })
        Tone(b, 0.5, 1.5, t => Mtof(m) * (1 + 0.006 * Math.Sin(2 * Math.PI * 5 * t) * Math.Min(1, t)), (ph, t) => Brass(ph, 0.6 + 0.4 * Math.Exp(-t * 2)),
            Adsr(0.02, 0.2, 0.75, 0.5, 1.5), m == 60 ? 0.5 : 0.35);
    Echo(b, 0.09, 0.35, 0.3);
    Save("fanfare", b);
}

{   // Level-up: schnelles 8-Bit-Arpeggio nach oben
    var b = Buf(1.2);
    int[] notes = { 60, 64, 67, 72, 76, 79, 84 };
    for (int k = 0; k < notes.Length; k++)
    {
        bool last = k == notes.Length - 1;
        double dur = last ? 0.6 : 0.065;
        int note = notes[k];
        Tone(b, k * 0.065, dur, _ => Mtof(note), (ph, _) => Square(ph, 0.25), last ? Decay(5) : Adsr(0.002, 0.02, 0.8, 0.01, dur), 0.5);
    }
    Echo(b, 0.11, 0.3, 0.3);
    Save("level-up", b);
}

{   // Münze (8-Bit): zwei helle Töne
    var b = Buf(0.6);
    Tone(b, 0, 0.07, _ => Mtof(91), (ph, _) => Square(ph, 0.5), Adsr(0.002, 0.01, 0.9, 0.005, 0.07), 0.5);
    Tone(b, 0.07, 0.5, _ => Mtof(96), (ph, _) => Square(ph, 0.5), Decay(7), 0.5);
    Save("muenze-8bit", b);
}

{   // Game Over: absteigende 8-Bit-Melodie
    var b = Buf(2.4);
    int[] notes = { 67, 66, 65, 64, 60 };
    double t0 = 0;
    for (int k = 0; k < notes.Length; k++)
    {
        bool last = k == notes.Length - 1;
        double dur = last ? 1.0 : 0.26;
        int note = notes[k];
        Tone(b, t0, dur, t => Mtof(note) * (last ? 1 + 0.02 * Math.Sin(2 * Math.PI * 6 * t) : 1),
            (ph, _) => Square(ph, 0.5), last ? Decay(2.2, 0.005) : Adsr(0.003, 0.05, 0.7, 0.05, dur), 0.45);
        Tone(b, t0, dur, _ => Mtof(note - 24), (ph, _) => Tri(ph), Adsr(0.003, 0.05, 0.9, 0.05, dur), 0.5);
        t0 += dur;
    }
    Save("game-over", b);
}

{   // Power-up: steigende Tonfolge mit Triller
    var b = Buf(1.0);
    Tone(b, 0, 0.9, t => Mtof(55 + 30 * t / 0.9 + 4 * Frac(t * 25)), (ph, _) => Square(ph, 0.3), Adsr(0.01, 0.1, 0.8, 0.2, 0.9), 0.4);
    Save("power-up", b);
}

{   // Sprung (8-Bit)
    var b = Buf(0.3);
    Tone(b, 0, 0.22, t => 280 * Math.Pow(3.2, t / 0.22), (ph, _) => Square(ph, 0.5), Adsr(0.002, 0.05, 0.7, 0.06, 0.22), 0.5);
    Save("sprung-8bit", b);
}

{   // Fehler (8-Bit): zwei tiefe Brummtöne
    var b = Buf(0.7);
    Tone(b, 0, 0.16, _ => 196, (ph, _) => Square(ph, 0.5), Adsr(0.003, 0.02, 0.9, 0.02, 0.16), 0.5);
    Tone(b, 0.2, 0.42, _ => 147, (ph, _) => Square(ph, 0.5), Adsr(0.003, 0.05, 0.9, 0.1, 0.42), 0.5);
    LowPass(b, 3500);
    Save("fehler-8bit", b);
}

{   // Bass-Boom: tiefer, satter Einschlag (für Pointen)
    var b = Buf(2.2);
    Tone(b, 0, 2.2, t => 42 + 70 * Math.Exp(-t * 22), (ph, _) => Sine(ph), Decay(2.0, 0.002));
    Tone(b, 0, 2.2, t => 84 + 140 * Math.Exp(-t * 22), (ph, _) => Sine(ph), Decay(3.0, 0.002), 0.35);
    Noise(b, 0, 0.04, Decay(90, 0.0005), 0.6, 0.3);
    Saturate(b, 2.5);
    LowPass(b, 900);
    Save("bass-boom", b);
}

{   // Bass-Drop: abfallender Sub-Bass
    var b = Buf(2.8);
    Tone(b, 0, 2.7, t => 30 + 220 * Math.Exp(-t * 1.6), (ph, _) => Sine(ph) + 0.3 * Saw(ph), Adsr(0.01, 0.2, 0.9, 0.6, 2.7));
    Saturate(b, 3);
    LowPass(b, 1200);
    Save("bass-drop", b);
}

{   // Dramatik: „Dun dun duuun“ – Streicher-Akkorde mit Tremolo
    var b = Buf(3.2);
    void Stab(double start, double dur, int[] chord, double trem)
    {
        foreach (var m in chord)
            Tone(b, start, dur, t => Mtof(m) * (1 + 0.003 * Math.Sin(2 * Math.PI * (4.5 + m % 3) * t)),
                (ph, t) => (Saw(ph) + 0.6 * Saw(ph * 1.003)) * (1 - trem * 0.5 * (1 + Math.Sin(2 * Math.PI * 9 * t))),
                Adsr(0.015, 0.12, 0.75, dur > 1 ? 0.9 : 0.08, dur), 0.25);
    }
    Stab(0, 0.28, new[] { 38, 50, 53, 57 }, 0);
    Stab(0.42, 0.28, new[] { 38, 50, 53, 57 }, 0);
    Stab(0.9, 2.2, new[] { 37, 49, 52, 55, 58 }, 0.4);
    LowPass(b, 2800);
    Echo(b, 0.12, 0.4, 0.3);
    Save("dramatik", b);
}

foreach (var (name, up) in new[] { ("slide-pfeife-hoch", true), ("slide-pfeife-runter", false) })
{   // Lotus-/Slide-Pfeife
    var b = Buf(0.9);
    Func<double, double> f = t => { double x = Math.Clamp(t / 0.7, 0, 1); x = x * x * (3 - 2 * x); return (up ? 500 * Math.Pow(4, x) : 2000 * Math.Pow(0.25, x)) * (1 + 0.012 * Math.Sin(2 * Math.PI * 7 * t)); };
    Tone(b, 0, 0.85, f, (ph, _) => Sine(ph) + 0.08 * Sine(2 * ph), Adsr(0.03, 0.1, 0.9, 0.12, 0.85));
    Noise(b, 0, 0.85, Adsr(0.03, 0.1, 0.9, 0.12, 0.85), 0.04, 0.5);
    Save(name, b);
}

{   // Plattenkratzer: kurzes Musikstück, vor und zurück „gescratcht“
    var src = Buf(2.0);
    foreach (var m in new[] { 45, 52, 57, 60, 64 })
        Tone(src, 0, 2.0, _ => Mtof(m), (ph, _) => Saw(ph), _ => 0.2);
    Noise(src, 0, 2.0, _ => 1, 0.25, 0.6);
    var b = Buf(0.75);
    double pos = 0.6 * SR;
    for (int i = 0; i < b.Length; i++)
    {
        double t = (double)i / SR;
        double speed = 3.2 * Math.Sin(2 * Math.PI * 2.7 * t) * (1 - t / 0.9);
        pos = Math.Clamp(pos + speed, 1, src.Length - 2);
        int p = (int)pos;
        double fr = pos - p;
        b[i] = (float)((src[p] * (1 - fr) + src[p + 1] * fr) * Math.Min(1, Math.Abs(speed) * 1.5));
    }
    LowPass(b, 5000);
    Save("plattenkratzer", b);
}

{   // Roboter: zufällige Piepser mit Ringmodulation
    var b = Buf(1.0);
    for (int k = 0; k < 8; k++)
    {
        double f = 300 + rnd.Next(0, 9) * 110;
        Tone(b, k * 0.11, 0.09, _ => f, (ph, t) => Square(ph, 0.5) * Sine(t * 55), Adsr(0.003, 0.02, 0.8, 0.02, 0.09), 0.5);
    }
    LowPass(b, 4000);
    Save("roboter", b);
}

{   // UFO: schwebendes Wabern
    var b = Buf(2.6);
    Tone(b, 0, 2.6, t => 700 + 280 * Math.Sin(2 * Math.PI * 7 * t) + 150 * Math.Sin(2 * Math.PI * 0.6 * t), (ph, _) => Sine(ph) + 0.25 * Tri(2 * ph), Adsr(0.3, 0.2, 0.9, 0.6, 2.6), 0.6);
    Echo(b, 0.13, 0.4, 0.3);
    Save("ufo", b);
}

{   // Teleport: glitzernder Anstieg
    var b = Buf(1.2);
    Tone(b, 0, 1.0, t => 220 * Math.Pow(10, t), (ph, t) => Sine(ph + 0.6 * Sine(ph * 2.01)), Adsr(0.05, 0.1, 0.8, 0.3, 1.0), 0.6);
    for (int k = 0; k < 10; k++)
    {
        double f = 2000 + rnd.Next(0, 3000);
        Tone(b, 0.1 * k, 0.15, _ => f, (ph, _) => Sine(ph), Decay(25), 0.15);
    }
    Echo(b, 0.09, 0.35, 0.3);
    Save("teleport", b);
}

{   // Countdown 3-2-1-Los
    var b = Buf(3.9);
    for (int k = 0; k < 3; k++)
        Tone(b, k, 0.16, _ => 880, (ph, _) => Sine(ph) + 0.2 * Square(ph), Adsr(0.003, 0.03, 0.8, 0.04, 0.16), 0.6);
    Tone(b, 3, 0.8, _ => 1760, (ph, _) => Sine(ph) + 0.2 * Square(ph), Adsr(0.003, 0.05, 0.8, 0.3, 0.8), 0.6);
    Save("countdown", b);
}

{   // Spannung: Riser aus Rauschen und steigendem Ton
    var b = Buf(3.2);
    Tone(b, 0, 3.0, t => 90 * Math.Pow(8, t / 3.0), (ph, _) => Saw(ph) + Saw(ph * 1.01), t => Math.Pow(t / 3.0, 2), 0.3);
    var n = Buf(3.2);
    Noise(n, 0, 3.0, t => Math.Pow(t / 3.0, 2.5), 0.5, 0.9);
    for (int i = 0; i < b.Length; i++) b[i] += n[i];
    LowPass(b, 6000);
    Save("spannung", b);
}

{   // Zensur-Piep
    var b = Buf(0.8);
    Tone(b, 0, 0.75, _ => 1000, (ph, _) => Sine(ph), Adsr(0.005, 0.01, 1, 0.01, 0.75));
    Save("zensur-piep", b);
}

{   // Sieg-Jingle (8-Bit)
    var b = Buf(1.9);
    (int m, double d)[] mel = { (72, 0.11), (76, 0.11), (79, 0.11), (84, 0.22), (79, 0.11), (84, 0.8) };
    double t0 = 0;
    foreach (var (m, d) in mel)
    {
        Tone(b, t0, d, _ => Mtof(m), (ph, _) => Square(ph, 0.25), d > 0.5 ? Decay(3) : Adsr(0.003, 0.03, 0.8, 0.02, d), 0.4);
        Tone(b, t0, d, _ => Mtof(m - 24), (ph, _) => Tri(ph), Adsr(0.003, 0.03, 0.9, 0.03, d), 0.5);
        t0 += d;
    }
    Echo(b, 0.1, 0.3, 0.25);
    Save("sieg-jingle", b);
}

{   // Richtig (Quiz-Ding-Ding)
    var b = Buf(1.4);
    foreach (var (start, m) in new[] { (0.0, 88), (0.13, 95) })
    {
        Tone(b, start, 1.2, _ => Mtof(m), (ph, _) => Sine(ph), Decay(4), 0.6);
        Tone(b, start, 1.2, _ => Mtof(m) * 2.76, (ph, _) => Sine(ph), Decay(9), 0.15);
    }
    Save("richtig", b);
}

MediaFoundationApi.Shutdown();
