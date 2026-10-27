using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using WKey = System.Windows.Input.Key;

namespace QuickNotchCs;

public sealed class AiSection : Section
{
    public string InputText = "";
    private bool _isFocused = true;
    private readonly List<ChatMessage> _messages = new();
    private readonly List<string> _codeSnippets = new();
    private readonly List<AeroChatSession> _sessions = new();
    private string? _currentSessionId;
    private bool _showHistory;
    private readonly Spring _historySpring = new(0.0);
    private string? _error;
    private bool _loading;
    private double _maxScroll;
    private bool _autoScrollToBottom = true;
    private double _copiedFeedbackTime;
    private int _copiedCodeIdx = -1;
    private Point _fullBotCenterScreen;
    private readonly Spring _fullBotEyeSpringX = new();
    private readonly Spring _fullBotEyeSpringY = new();
    private readonly Spring _settingsSpring = new(0.0);
    private double _talkingUntil;
    private bool _showSettings;

    public bool IsGenerating => _loading;

    public AiSection()
    {
        Key = "ai";
        Title = "Аэро";
    }

    public override void Attach(IslandView isl)
    {
        _sessions.Clear();
        _sessions.AddRange(StoreService.LoadAeroChats());
        if (_sessions.Count > 0)
        {
            var latest = _sessions[0];
            _currentSessionId = latest.Id;
            _messages.Clear();
            _messages.AddRange(latest.Messages);
        }
    }

    public override void Enter(IslandView isl)
    {
        _isFocused = true;
        isl.Focus();
    }

    private void SaveCurrentSession()
    {
        if (_messages.Count == 0) return;

        var existing = _sessions.FirstOrDefault(s => s.Id == _currentSessionId);
        if (existing == null)
        {
            var firstUserMsg = _messages.FirstOrDefault(m => m.Role == "user");
            string title = firstUserMsg != null ? CleanTitle(firstUserMsg.Text) : Loc.T("Беседа с Аэро", "Chat with Aero", "Aero Sohbeti");

            existing = new AeroChatSession
            {
                Id = _currentSessionId ?? Guid.NewGuid().ToString("N"),
                Title = title,
                CreatedAt = DateTime.Now,
                UpdatedAt = DateTime.Now,
                Messages = new List<ChatMessage>(_messages)
            };
            _currentSessionId = existing.Id;
            _sessions.Insert(0, existing);
        }
        else
        {
            existing.UpdatedAt = DateTime.Now;
            existing.Messages = new List<ChatMessage>(_messages);
            if (_sessions.IndexOf(existing) > 0)
            {
                _sessions.Remove(existing);
                _sessions.Insert(0, existing);
            }
        }

        if (_sessions.Count > 50) _sessions.RemoveRange(50, _sessions.Count - 50);
        StoreService.SaveAeroChats(_sessions);
    }

    private static string CleanTitle(string text)
    {
        string t = text.Trim().Replace("\r", " ").Replace("\n", " ");
        while (t.Contains("  ")) t = t.Replace("  ", " ");
        if (t.Length > 36) t = t[..33] + "…";
        return string.IsNullOrWhiteSpace(t) ? Loc.T("Новая беседа", "New Chat", "Yeni Sohbet") : t;
    }

    // ==================== PARSING MARKDOWN CODE BLOCKS ====================
    private readonly struct MsgSegment
    {
        public readonly bool IsCode;
        public readonly string Language;
        public readonly string Content;

        public MsgSegment(bool isCode, string language, string content)
        {
            IsCode = isCode;
            Language = language;
            Content = content;
        }
    }

    private static List<MsgSegment> ParseSegments(string text)
    {
        var list = new List<MsgSegment>();
        if (string.IsNullOrEmpty(text)) return list;

        int idx = 0;
        while (idx < text.Length)
        {
            int codeStart = text.IndexOf("```", idx, StringComparison.Ordinal);
            if (codeStart < 0)
            {
                string rest = text[idx..].Trim();
                if (!string.IsNullOrEmpty(rest))
                    list.Add(new MsgSegment(false, "", rest));
                break;
            }

            if (codeStart > idx)
            {
                string pre = text[idx..codeStart].Trim();
                if (!string.IsNullOrEmpty(pre))
                    list.Add(new MsgSegment(false, "", pre));
            }

            int langEnd = text.IndexOf('\n', codeStart + 3);
            if (langEnd < 0)
            {
                string lang = text[(codeStart + 3)..].Trim();
                list.Add(new MsgSegment(true, lang, ""));
                break;
            }

            string language = text[(codeStart + 3)..langEnd].Trim();
            int codeEnd = text.IndexOf("```", langEnd + 1, StringComparison.Ordinal);
            if (codeEnd < 0)
            {
                string code = text[(langEnd + 1)..];
                list.Add(new MsgSegment(true, language, code));
                break;
            }

            string codeContent = text[(langEnd + 1)..codeEnd];
            list.Add(new MsgSegment(true, language, codeContent));
            idx = codeEnd + 3;
        }

        return list;
    }

    private static List<MsgSegment> GetSegments(ChatMessage msg)
    {
        if (msg.CachedSegments is List<MsgSegment> cached && msg.Text == msg.LastParsedText)
            return cached;

        var list = ParseSegments(msg.Text);
        msg.CachedSegments = list;
        msg.LastParsedText = msg.Text;
        return list;
    }

    // ==================== AERO FULL-BODY ROBOT COMPANION ====================
    private void PaintFullBodyRobot(IslandView isl, Painter p, Rect stage)
    {
        double botHv = isl.Hv("ai:pet_bot");
        var stageBg = Theme.Mix(Theme.Surface, Theme.Surface2, 0.4 + 0.35 * botHv);
        var stageBorder = Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.25 + 0.4 * botHv);
        p.Card(stage, stageBg, 16, stageBorder);
        isl.AddHit("ai:pet_bot", stage);

        double cx = stage.Left + stage.Width / 2.0;

        // 1. Верхний статусный бейдж
        var statusRect = new Rect(stage.Left + 12, stage.Top + 10, stage.Width - 24, 22);
        if (_loading)
        {
            p.Card(statusRect, Theme.Mix(Theme.Surface2, Theme.AccentHot, 0.35), 11, Theme.AccentHot);
            p.Label("🧠 " + Loc.T("Анализирует...", "Thinking...", "Düşünüyor..."), statusRect, 10, Theme.AccentHot, true, HAlign.Center);
        }
        else if (_showSettings)
        {
            p.Card(statusRect, Theme.Mix(Theme.Surface2, Theme.Accent, 0.4), 11, Theme.Accent);
            p.Label("⚙ " + Loc.T("Настройки", "Settings", "Ayarlar"), statusRect, 10, Colors.White, true, HAlign.Center);
        }
        else if (_messages.Count > 0 && _messages.Last().Role == "model")
        {
            p.Card(statusRect, Theme.Mix(Theme.Surface2, Theme.Accent, 0.35), 11, Theme.Accent);
            p.Label("💬 " + Loc.T("Аэро говорит", "Aero speaks", "Aero konuşuyor"), statusRect, 10, Colors.White, true, HAlign.Center);
        }
        else
        {
            p.Card(statusRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.6), 11, Theme.BorderSubtle);
            p.Label("✦ " + Loc.T("Ассистент Аэро", "Assistant Aero", "Asistan Aero"), statusRect, 10, Theme.Muted, true, HAlign.Center);
        }

        // 2. Физика парения и прыжка (непрерывная фаза BotAnimPhase, без рывков)
        double phase = isl.BotAnimPhase;
        double bob = Math.Sin(phase) * 2.8 + isl.SpBotHop.X;
        double cy = stage.Top + 130 + bob;
        double floorY = stage.Bottom - 44;

        // Расчёт параллакса и слежения за курсором (3D Look-at)
        double dx = isl.GlobalMousePos.X - _fullBotCenterScreen.X;
        double dy = isl.GlobalMousePos.Y - _fullBotCenterScreen.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double maxEye = 3.8;
        double targetEyeX = 0, targetEyeY = 0;
        if (dist > 3.0)
        {
            double factor = Math.Min(dist * 0.006, maxEye);
            targetEyeX = (dx / dist) * factor;
            targetEyeY = (dy / dist) * factor;
        }

        _fullBotEyeSpringX.Target = targetEyeX;
        _fullBotEyeSpringY.Target = targetEyeY;
        _fullBotEyeSpringX.Step(0.016, 320, 23);
        _fullBotEyeSpringY.Step(0.016, 320, 23);

        double tiltX = _fullBotEyeSpringX.X * 0.45;
        double tiltY = _fullBotEyeSpringY.X * 0.35;

        // 3. Светящаяся голографическая антигравитационная платформа
        Point floorCenter = new Point(cx, floorY);
        p.Dc.DrawEllipse(p.B(Color.FromArgb(32, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B)), null, floorCenter, 46, 12);
        p.Dc.DrawEllipse(null, p.P(Theme.Mix(Theme.Accent, Colors.Transparent, 0.5), 1.5), floorCenter, 42, 10.5);
        p.Dc.DrawEllipse(null, p.P(Theme.Mix(Theme.AccentHot, Colors.Transparent, 0.65), 1.0), floorCenter, 24, 6.0);

        // Вращающиеся энергетические засечки по периметру платформы
        for (int ni = 0; ni < 4; ni++)
        {
            double angle = phase * 0.7 + ni * (Math.PI / 2.0);
            double nx = cx + Math.Cos(angle) * 40.0;
            double ny = floorY + Math.Sin(angle) * 10.0;
            p.Circle(new Point(nx, ny), 1.4, Theme.AccentHot);
        }

        // Парящие квантовые частицы энергии
        for (int pi = 0; pi < 4; pi++)
        {
            double pCycle = (phase * 22.0 + pi * 14.0) % 52.0;
            double pY = floorY - pCycle;
            double pX = cx + Math.Sin(phase * 1.5 + pi * 1.8) * (14.0 + pi * 3.5);
            double pAlpha = Math.Sin((pCycle / 52.0) * Math.PI);
            byte a = (byte)(150 * Math.Clamp(pAlpha, 0.0, 1.0));
            p.Circle(new Point(pX, pY), 1.3, Color.FromArgb(a, Theme.AccentHot.R, Theme.AccentHot.G, Theme.AccentHot.B));
        }

        // 4. Мягкий плазменный световой луч антигравитации
        double thrusterY = cy + 40;
        if (floorY > thrusterY)
        {
            byte jetAlpha = (byte)(55 + 30 * Math.Sin(phase * 2.8));
            Color jetOuter = Color.FromArgb((byte)(jetAlpha * 0.6), Theme.Accent.R, Theme.Accent.G, Theme.Accent.B);
            Color jetInner = Color.FromArgb(jetAlpha, Theme.AccentHot.R, Theme.AccentHot.G, Theme.AccentHot.B);

            // Внешний мягкий ореол
            var outerFig = new PathFigure { StartPoint = new Point(cx - 8, thrusterY), IsClosed = true, IsFilled = true };
            outerFig.Segments.Add(new LineSegment(new Point(cx - 24, floorY - 2), false));
            outerFig.Segments.Add(new LineSegment(new Point(cx + 24, floorY - 2), false));
            outerFig.Segments.Add(new LineSegment(new Point(cx + 8, thrusterY), false));
            var outerGeom = new PathGeometry();
            outerGeom.Figures.Add(outerFig);
            p.Dc.DrawGeometry(p.B(jetOuter), null, outerGeom);

            // Внутренний сфокусированный луч
            var innerFig = new PathFigure { StartPoint = new Point(cx - 4, thrusterY), IsClosed = true, IsFilled = true };
            innerFig.Segments.Add(new LineSegment(new Point(cx - 12, floorY - 2), false));
            innerFig.Segments.Add(new LineSegment(new Point(cx + 12, floorY - 2), false));
            innerFig.Segments.Add(new LineSegment(new Point(cx + 4, thrusterY), false));
            var innerGeom = new PathGeometry();
            innerGeom.Figures.Add(innerFig);
            p.Dc.DrawGeometry(p.B(jetInner), null, innerGeom);
        }

        // 5. Нижняя дюза двигателя (Engine Pod)
        var thrusterRect = new Rect(cx - 10 + tiltX * 0.3, cy + 34, 20, 10);
        p.Card(thrusterRect, Color.FromRgb(16, 18, 24), 5, Theme.AccentHot);
        p.Line(new Point(thrusterRect.Left + 3, thrusterRect.Bottom - 1), new Point(thrusterRect.Right - 3, thrusterRect.Bottom - 1), Theme.AccentHot, 1.6);

        // Цвета керамического корпуса (светлая/тёмная тема)
        Color torsoBg = Theme.IsLight ? Color.FromRgb(244, 246, 252) : Color.FromRgb(28, 31, 42);
        Color torsoBorder = Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.45);

        // 6. Аэродинамичный капсульный торс (Aerodynamic Ceramic Body)
        double torsoW = 48;
        double torsoH = 40;
        var torsoRect = new Rect(cx - torsoW / 2.0 + tiltX * 0.4, cy + 2 + tiltY * 0.3, torsoW, torsoH);
        p.Card(torsoRect, torsoBg, 18, torsoBorder);

        // Глянцевый световой хайлайт по верхней кромке корпуса
        var bodyHighlight = new Rect(torsoRect.Left + 6, torsoRect.Top + 2, torsoRect.Width - 12, 6);
        p.Card(bodyHighlight, Color.FromArgb(40, 255, 255, 255), 3);

        // Тонкая технологичная панельная линия корпуса
        p.Line(new Point(torsoRect.Left + 5, torsoRect.Top + 24), new Point(torsoRect.Right - 5, torsoRect.Top + 24),
            Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.3), 1.0);

        // Квантовый ARC-реактор на груди
        Point coreCenter = new Point(cx + tiltX * 0.5, cy + 16 + tiltY * 0.3);
        double corePulse = 1.0 + 0.14 * Math.Sin(phase * 2.0);
        Color coreColor = _loading ? Theme.AccentHot : (isl.BotEmotion == "love" ? Theme.Red : Theme.Accent);

        // Внешнее пульсирующее энергокольцо
        p.Dc.DrawEllipse(null, p.P(Theme.Mix(coreColor, Colors.Transparent, 0.45), 1.4), coreCenter, 9.5 * corePulse, 9.5 * corePulse);

        // Орбитальные энергетические микро-точки
        for (int oi = 0; oi < 3; oi++)
        {
            double oAngle = phase * 2.5 + oi * (Math.PI * 2.0 / 3.0);
            double ox = coreCenter.X + Math.Cos(oAngle) * (8.5 * corePulse);
            double oy = coreCenter.Y + Math.Sin(oAngle) * (8.5 * corePulse);
            p.Circle(new Point(ox, oy), 1.2, coreColor);
        }

        // Светящееся ядро
        p.Circle(coreCenter, 5.0, Theme.Mix(coreColor, Colors.Transparent, 0.2));
        p.Circle(coreCenter, 3.2, coreColor);
        string coreIcon = isl.BotEmotion == "love" ? "♥" : (_loading ? "✦" : "⚡");
        p.Label(coreIcon, new Rect(coreCenter.X - 5, coreCenter.Y - 5, 10, 10), 7.0, Colors.White, true, HAlign.Center);

        // 7. Парящие магнитные ручки (Floating Magnetic Hover Hands)
        double lWave = Math.Sin(phase * 1.4 + 0.6) * 3.0;
        Point lHandPos;
        if (_loading)
        {
            lHandPos = new Point(cx - 20 + tiltX * 0.5, cy - 4 + lWave * 0.5);
        }
        else if (botHv > 0.3)
        {
            lHandPos = new Point(cx - 36 + tiltX * 0.6, cy + 6 - Math.Abs(Math.Sin(phase * 3.0)) * 8.0);
        }
        else
        {
            lHandPos = new Point(cx - 34 + tiltX * 0.5, cy + 18 + lWave);
        }

        double rWave = Math.Sin(phase * 1.4 + 2.2) * 3.0;
        Point rHandPos;
        if (_talkingUntil > Clock.Now)
        {
            double talkGesture = Math.Sin(phase * 4.0) * 6.0;
            rHandPos = new Point(cx + 38 + tiltX * 0.6, cy + 12 + talkGesture);
        }
        else if (_messages.Count > 0 || _showSettings)
        {
            rHandPos = new Point(cx + 36 + tiltX * 0.6, cy + 14 + rWave);
        }
        else
        {
            double waveGesture = Math.Sin(phase * 2.2) * 6.0;
            rHandPos = new Point(cx + 35 + tiltX * 0.5, cy + 8 + waveGesture);
        }

        // Магнитная световая связь левитации
        p.Line(new Point(cx - 20, cy + 12), lHandPos, Color.FromArgb(45, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B), 1.2);
        p.Line(new Point(cx + 20, cy + 12), rHandPos, Color.FromArgb(45, Theme.Accent.R, Theme.Accent.G, Theme.Accent.B), 1.2);

        // Левая перчатка-капсула (обтекаемая форма 12x18, r=6)
        var lHandRect = new Rect(lHandPos.X - 6, lHandPos.Y - 9, 12, 18);
        p.Card(lHandRect, torsoBg, 6, torsoBorder);
        p.Card(new Rect(lHandRect.Left + 2, lHandRect.Top + 2, lHandRect.Width - 4, 3), Theme.Accent, 1.5);

        // Правая перчатка-капсула
        var rHandRect = new Rect(rHandPos.X - 6, rHandPos.Y - 9, 12, 18);
        p.Card(rHandRect, torsoBg, 6, torsoBorder);
        p.Card(new Rect(rHandRect.Left + 2, rHandRect.Top + 2, rHandRect.Width - 4, 3), Theme.AccentHot, 1.5);

        // 8. Шея
        p.Card(new Rect(cx - 6 + tiltX * 0.4, cy - 4 + tiltY * 0.3, 12, 7), Color.FromRgb(20, 22, 28), 2.5);

        // 9. Голова и сферический шлем
        double headW = 62;
        double headH = 46;
        double headY = cy - 25;
        Point headCenter = new Point(cx + tiltX, headY + tiltY);
        var helmetRect = new Rect(headCenter.X - headW / 2.0, headCenter.Y - headH / 2.0, headW, headH);

        try
        {
            _fullBotCenterScreen = isl.PointToScreen(new Point(cx, stage.Top + 130 - 25));
        }
        catch { }

        // Ушки-турбинки (Aero Ear Pods)
        var lEarRect = new Rect(helmetRect.Left - 4, headCenter.Y - 9, 5, 18);
        var rEarRect = new Rect(helmetRect.Right - 1, headCenter.Y - 9, 5, 18);
        p.Card(lEarRect, Theme.Mix(Theme.Surface3, Theme.Accent, 0.4), 2.5, Theme.Accent);
        p.Card(rEarRect, Theme.Mix(Theme.Surface3, Theme.Accent, 0.4), 2.5, Theme.AccentHot);

        // Корпус шлема (сферический, радиус 18)
        p.Card(helmetRect, torsoBg, 18, torsoBorder);

        // Глянцевый световой блик сверху шлема (Ceramic Highlight)
        var helmetHighlight = new Rect(helmetRect.Left + 10, helmetRect.Top + 3, helmetRect.Width - 20, 7);
        p.Card(helmetHighlight, Color.FromArgb(50, 255, 255, 255), 3.5);

        // Антенна с неоновым маячком
        Point antBase = new Point(headCenter.X, helmetRect.Top);
        Point antTip = new Point(headCenter.X, helmetRect.Top - 13);
        p.Line(antBase, antTip, Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.6), 1.8);
        Color tipColor = (_loading || isl.BotEmotionTimer > 0) ? Theme.AccentHot : Theme.Accent;
        p.Circle(antTip, 4.2, Theme.Mix(tipColor, Colors.Transparent, 0.4));
        p.Circle(antTip, 2.8, tipColor);

        // Волны мысли от антенны при загрузке
        if (_loading)
        {
            double ringFrac = (phase * 0.7) % 1.0;
            byte ringAlpha = (byte)(160 * (1.0 - ringFrac));
            p.Dc.DrawEllipse(null, p.P(Color.FromArgb(ringAlpha, Theme.AccentHot.R, Theme.AccentHot.G, Theme.AccentHot.B), 1.2),
                antTip, 4.0 + ringFrac * 11.0, 4.0 + ringFrac * 11.0);
        }

        // 10. Глубокий панорамный OLED-визор
        var visorRect = new Rect(headCenter.X - 24, headCenter.Y - 14, 48, 28);
        p.Card(visorRect, Color.FromRgb(8, 10, 16), 12, Color.FromArgb(55, 255, 255, 255));

        // Дугообразный стеклянный блик (Apple Curved Glass Reflection)
        var glassReflection = new Rect(visorRect.Left + 5, visorRect.Top + 2, visorRect.Width - 10, 6);
        p.Card(glassReflection, Color.FromArgb(45, 255, 255, 255), 3);

        // 11. Неоновые кибер-глазки и мимика
        double lx = headCenter.X - 10 + _fullBotEyeSpringX.X;
        double rx = headCenter.X + 10 + _fullBotEyeSpringX.X;
        double eyeY = headCenter.Y + _fullBotEyeSpringY.X;
        Color eyeColor = (_loading || isl.BotEmotionTimer > 0) ? Theme.AccentHot : Theme.Accent;

        string emotion = isl.BotEmotion;
        double emoTimer = isl.BotEmotionTimer;

        if (emotion == "love" && emoTimer > 0)
        {
            p.Label("♥", new Rect(lx - 6, eyeY - 8, 12, 16), 13, Theme.Red, true, HAlign.Center);
            p.Label("♥", new Rect(rx - 6, eyeY - 8, 12, 16), 13, Theme.Red, true, HAlign.Center);
            p.Circle(new Point(lx, eyeY + 6), 2.2, Color.FromArgb(200, 255, 120, 160));
            p.Circle(new Point(rx, eyeY + 6), 2.2, Color.FromArgb(200, 255, 120, 160));
        }
        else if (emotion == "wink" && emoTimer > 0)
        {
            p.Polyline(eyeColor, 2.2, new Point(lx - 4, eyeY + 1), new Point(lx, eyeY - 3), new Point(lx + 4, eyeY + 1));
            p.Label("★", new Rect(rx - 6, eyeY - 7, 12, 15), 12, Theme.Yellow, true, HAlign.Center);
            p.Circle(new Point(lx, eyeY + 5), 2.0, Color.FromArgb(180, 255, 120, 160));
        }
        else if (emotion == "stars" && emoTimer > 0)
        {
            p.Label("★", new Rect(lx - 6, eyeY - 8, 12, 16), 13, Theme.Yellow, true, HAlign.Center);
            p.Label("★", new Rect(rx - 6, eyeY - 8, 12, 16), 13, Theme.Yellow, true, HAlign.Center);
            p.Circle(new Point(lx, eyeY + 5), 2.0, Color.FromArgb(180, 255, 200, 80));
            p.Circle(new Point(rx, eyeY + 5), 2.0, Color.FromArgb(180, 255, 200, 80));
        }
        else if (emotion == "cool" && emoTimer > 0)
        {
            var leftLens = new Rect(headCenter.X - 20, headCenter.Y - 6, 18, 12);
            var rightLens = new Rect(headCenter.X + 2, headCenter.Y - 6, 18, 12);
            p.Card(leftLens, Color.FromRgb(15, 15, 22), 3.0, Theme.AccentHot);
            p.Card(rightLens, Color.FromRgb(15, 15, 22), 3.0, Theme.AccentHot);
            p.Line(new Point(headCenter.X - 2, headCenter.Y - 1), new Point(headCenter.X + 2, headCenter.Y - 1), Theme.AccentHot, 1.8);
            p.Line(new Point(leftLens.Left + 2, leftLens.Top + 2), new Point(leftLens.Left + 6, leftLens.Bottom - 2), Colors.White, 1.2);
            p.Line(new Point(rightLens.Left + 2, rightLens.Top + 2), new Point(rightLens.Left + 6, rightLens.Bottom - 2), Colors.White, 1.2);
        }
        else if (emotion == "surprised" && emoTimer > 0)
        {
            p.Dc.DrawEllipse(null, p.P(Theme.AccentHot, 2.0), new Point(lx, eyeY), 5.5, 5.5);
            p.Dc.DrawEllipse(null, p.P(Theme.AccentHot, 2.0), new Point(rx, eyeY), 5.5, 5.5);
            p.Circle(new Point(lx, eyeY), 2.2, Theme.Text);
            p.Circle(new Point(rx, eyeY), 2.2, Theme.Text);
        }
        else if (_loading)
        {
            double scan = Math.Sin(phase * 2.8) * 13.0;
            Point scanPt = new Point(headCenter.X + scan, eyeY);
            p.Dc.DrawEllipse(null, p.P(Theme.AccentHot, 1.8), scanPt, 6.0, 6.0);
            p.Circle(scanPt, 3.5, Colors.White);
        }
        else if (Clock.Now < _talkingUntil)
        {
            p.Polyline(eyeColor, 2.4, new Point(lx - 4, eyeY + 1), new Point(lx, eyeY - 3), new Point(lx + 4, eyeY + 1));
            p.Polyline(eyeColor, 2.4, new Point(rx - 4, eyeY + 1), new Point(rx, eyeY - 3), new Point(rx + 4, eyeY + 1));

            double voiceY = eyeY + 7;
            for (int vi = -2; vi <= 2; vi++)
            {
                double barH = 2.0 + Math.Abs(Math.Sin(phase * 4.0 + vi * 1.1)) * 5.5;
                double bx = headCenter.X + vi * 4.5;
                p.Line(new Point(bx, voiceY - barH / 2.0), new Point(bx, voiceY + barH / 2.0), Theme.AccentHot, 1.6);
            }
        }
        else if (isl.BotBlinkState > 0.2)
        {
            p.Line(new Point(lx - 4, eyeY), new Point(lx + 4, eyeY), eyeColor, 2.2);
            p.Line(new Point(rx - 4, eyeY), new Point(rx + 4, eyeY), eyeColor, 2.2);
        }
        else
        {
            // Мягкий неоновый ореол (bloom)
            p.Circle(new Point(lx, eyeY), 5.5, Theme.Mix(eyeColor, Colors.Transparent, 0.55));
            p.Circle(new Point(rx, eyeY), 5.5, Theme.Mix(eyeColor, Colors.Transparent, 0.55));

            p.Card(new Rect(lx - 3.5, eyeY - 4.5, 7, 9), eyeColor, 3.5);
            p.Card(new Rect(rx - 3.5, eyeY - 4.5, 7, 9), eyeColor, 3.5);
            p.Circle(new Point(lx + 1, eyeY - 1.5), 1.3, Colors.White);
            p.Circle(new Point(rx + 1, eyeY - 1.5), 1.3, Colors.White);
        }

        // 12. Всплывающее облачко реплики при клике на робота
        if (isl.BotBubbleTime > 0 && !string.IsNullOrEmpty(isl.BotBubbleText))
        {
            double bubbleA = Ui.Clamp(isl.BotBubbleTime * 2.2);
            double bubbleW = Math.Min(260, p.TextWidth(isl.BotBubbleText, 10, true) + 22);
            double bubbleX = Math.Max(stage.Left + 4, cx - bubbleW / 2.0);
            var bubbleRect = new Rect(bubbleX, helmetRect.Top - 36, bubbleW, 23);

            using (p.WithOpacity(bubbleA))
            {
                p.Card(bubbleRect, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.95), 11, Theme.AccentHot);
                double tailX = Math.Max(bubbleRect.Left + 8, Math.Min(bubbleRect.Right - 8, cx));
                p.Polygon(Theme.Mix(Theme.Surface2, Theme.Surface3, 0.95),
                    new Point(tailX - 4, bubbleRect.Bottom),
                    new Point(tailX, bubbleRect.Bottom + 5),
                    new Point(tailX + 4, bubbleRect.Bottom));
                p.Label(isl.BotBubbleText, bubbleRect, 10, Theme.Text, true, HAlign.Center);
            }
        }

        // 13. Кнопка взаимодействия внизу сцены
        var petBtnRect = new Rect(stage.Left + 12, stage.Bottom - 32, stage.Width - 24, 24);
        petBtnRect = p.PressRect(isl, "ai:pet_bot", petBtnRect, 1.5);
        p.Card(petBtnRect, Theme.Mix(Theme.Surface2, Theme.Surface3, 0.7 + 0.3 * botHv), 12, Theme.BorderSubtle);
        p.Label("✨ " + Loc.T("Погладить Аэро", "Pet Aero", "Aero'yu Sev"), petBtnRect, 9.5, Theme.Muted, true, HAlign.Center);
    }

    // ==================== AERO INTELLIGENCE SETTINGS VIEW ====================
    private void PaintSettingsView(IslandView isl, Painter p, Rect r)
    {
        p.Card(r, Theme.Mix(Theme.Surface, Theme.Surface2, 0.45), 16, Theme.BorderSubtle);

        // Заголовок настроек
        p.Label(Loc.T("⚙ Настройки интеллекта Аэро", "⚙ Aero Intelligence Settings", "⚙ Aero Zeka Ayarları"),
            new Rect(r.Left + 20, r.Top + 14, 280, 24), 14, Theme.Text, true);

        // Кнопка Закрыть в правом верхнем углу
        var closeBtn = new Rect(r.Right - 84, r.Top + 12, 68, 26);
        var pressClose = p.PressRect(isl, "ai:toggle_settings", closeBtn, 1.5);
        p.Card(pressClose, Theme.Mix(Theme.Surface2, Theme.Surface3, isl.Hv("ai:toggle_settings")), 13, Theme.BorderSubtle);
        p.Label("✕ " + Loc.T("Назад", "Back", "Geri"), pressClose, 10, Theme.Text, true, HAlign.Center);
        isl.AddHit("ai:toggle_settings", closeBtn);

        p.Line(new Point(r.Left + 16, r.Top + 44), new Point(r.Right - 16, r.Top + 44), Theme.BorderSubtle, 1.0);

        // 1. Выбор провайдера (Gemini / OpenAI / DeepSeek)
        double py = r.Top + 54;
        p.Label(Loc.T("Провайдер нейросети:", "AI Provider:", "Yapay Zeka Sağlayıcısı:"),
            new Rect(r.Left + 20, py, 220, 18), 10.5, Theme.Muted, true);
        py += 22;

        var providers = new[]
        {
            ("gemini", "✦ Gemini", Loc.T("Google AI (бесплатный)", "Google AI (Free key)", "Google AI (Ücretsiz)")),
            ("openai", "🟢 OpenAI", Loc.T("GPT-4o / GPT-4o-mini", "GPT-4o / GPT-4o-mini", "GPT-4o / GPT-4o-mini")),
            ("deepseek", "🐳 DeepSeek", Loc.T("V3 / R1 Reasoner", "V3 / R1 Reasoner", "V3 / R1 Akıl Yürütme"))
        };

        double provCardW = (r.Width - 40 - 16) / 3.0;
        double provCardH = 48;
        for (int i = 0; i < providers.Length; i++)
        {
            var (pKey, pLabel, pDesc) = providers[i];
            bool isCur = isl.S.AiProvider == pKey;
            var cRect = new Rect(r.Left + 20 + i * (provCardW + 8), py, provCardW, provCardH);

            var bg = isCur ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.45) : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv($"ai:prov:{pKey}"));
            var border = isCur ? Theme.Accent : Theme.BorderSubtle;
            p.Card(cRect, bg, 12, border);

            p.Label(pLabel, new Rect(cRect.Left + 10, cRect.Top + 8, cRect.Width - 20, 18), 11.5, isCur ? Colors.White : Theme.Text, true);
            p.Label(pDesc, new Rect(cRect.Left + 10, cRect.Top + 26, cRect.Width - 20, 16), 9.5, isCur ? Theme.Mix(Colors.White, Theme.AccentHot, 0.4) : Theme.Muted);
            isl.AddHit($"ai:prov:{pKey}", cRect);
        }
        py += provCardH + 16;

        // 2. Режим работы: Fast vs Pro
        p.Label(Loc.T("Режим ответов:", "Response Mode:", "Yanıt Modu:"),
            new Rect(r.Left + 20, py, 220, 18), 10.5, Theme.Muted, true);
        py += 22;

        bool isFast = isl.S.AiMode == "fast";
        var modes = new[]
        {
            ("fast", "⚡ Fast", Loc.T("Моментальные ответы, экономия токенов", "Instant responses, lightweight", "Hızlı yanıtlar, hafif")),
            ("pro", "🧠 Pro", Loc.T("Глубокий анализ кода, логика и рассуждения", "Deep code review, reasoning", "Derin kod inceleme, mantık"))
        };

        double modeCardW = (r.Width - 40 - 10) / 2.0;
        double modeCardH = 46;
        for (int i = 0; i < modes.Length; i++)
        {
            var (mKey, mLabel, mDesc) = modes[i];
            bool isSel = (mKey == "fast" && isFast) || (mKey == "pro" && !isFast);
            var mRect = new Rect(r.Left + 20 + i * (modeCardW + 10), py, modeCardW, modeCardH);

            var bg = isSel ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.45) : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv($"ai:mode:{mKey}"));
            var border = isSel ? Theme.Accent : Theme.BorderSubtle;
            p.Card(mRect, bg, 12, border);

            p.Label(mLabel, new Rect(mRect.Left + 12, mRect.Top + 7, mRect.Width - 24, 18), 11.5, isSel ? Colors.White : Theme.Text, true);
            p.Label(mDesc, new Rect(mRect.Left + 12, mRect.Top + 25, mRect.Width - 24, 16), 9.5, isSel ? Theme.Mix(Colors.White, Theme.AccentHot, 0.4) : Theme.Muted);
            isl.AddHit($"ai:mode:{mKey}", mRect);
        }
        py += modeCardH + 16;

        // 3. API-ключ текущего провайдера
        string activeKey = AiService.GetActiveKey(isl.S);
        bool hasKey = !string.IsNullOrWhiteSpace(activeKey);

        p.Label(Loc.T($"API-ключ для {isl.S.AiProvider.ToUpperInvariant()}:", $"API Key for {isl.S.AiProvider.ToUpperInvariant()}:", $"{isl.S.AiProvider.ToUpperInvariant()} için API Anahtarı:"),
            new Rect(r.Left + 20, py, 300, 18), 10.5, Theme.Muted, true);
        py += 22;

        var keyBox = new Rect(r.Left + 20, py, r.Width - 40, 42);
        p.Card(keyBox, Theme.Surface, 12, hasKey ? Theme.BorderSubtle : Theme.Red);

        if (hasKey)
        {
            string masked = $"{activeKey[..Math.Min(8, activeKey.Length)]}••••••••••••••••";
            p.Label($"🔑 {masked}", new Rect(keyBox.Left + 14, keyBox.Top + 12, keyBox.Width - 210, 20), 11.5, Theme.Green, true);

            var editKeyBtn = new Rect(keyBox.Right - 180, keyBox.Top + 8, 92, 26);
            p.Card(editKeyBtn, Theme.Mix(Theme.Surface2, Theme.Surface3, isl.Hv("ai:key")), 10, Theme.BorderSubtle);
            p.Label(Loc.T("Изменить", "Change", "Değiştir"), editKeyBtn, 10, Theme.Text, true, HAlign.Center);
            isl.AddHit("ai:key", editKeyBtn);

            var delKeyBtn = new Rect(keyBox.Right - 80, keyBox.Top + 8, 70, 26);
            p.Card(delKeyBtn, Theme.Mix(Theme.Surface2, Theme.Red, 0.2 + 0.15 * isl.Hv("ai:clear_key")), 10, Theme.Red);
            p.Label(Loc.T("Удалить", "Delete", "Sil"), delKeyBtn, 10, Theme.Red, true, HAlign.Center);
            isl.AddHit("ai:clear_key", delKeyBtn);
        }
        else
        {
            p.Label(Loc.T("⚠ Ключ отсутствует (нужен для работы)", "⚠ Key is missing (required)", "⚠ Anahtar eksik (gerekli)"),
                new Rect(keyBox.Left + 14, keyBox.Top + 12, keyBox.Width - 150, 20), 11, Theme.Red, true);

            var addKeyBtn = new Rect(keyBox.Right - 130, keyBox.Top + 8, 120, 26);
            p.Card(addKeyBtn, Theme.Mix(Theme.Surface2, Theme.Red, 0.35 + 0.2 * isl.Hv("ai:key")), 10, Theme.Red);
            p.Label(Loc.T("🔑 Ввести ключ", "🔑 Enter Key", "🔑 Anahtar Gir"), addKeyBtn, 10, Colors.White, true, HAlign.Center);
            isl.AddHit("ai:key", addKeyBtn);
        }
        py += 42 + 16;

        // 4. Модель нейросети
        string curModel = isl.S.GetEffectiveAiModel();
        var modelBox = new Rect(r.Left + 20, py, r.Width - 40, 38);
        p.Card(modelBox, Theme.Surface, 10, Theme.BorderSubtle);

        p.Label(Loc.T("Модель: ", "Model: ", "Model: ") + curModel,
            new Rect(modelBox.Left + 14, modelBox.Top + 10, modelBox.Width - 140, 18), 10.5, Theme.Muted);

        var editModelBtn = new Rect(modelBox.Right - 110, modelBox.Top + 6, 100, 26);
        p.Card(editModelBtn, Theme.Mix(Theme.Surface2, Theme.Surface3, isl.Hv("ai:model_info")), 10, Theme.BorderSubtle);
        p.Label(Loc.T("✎ Настроить", "✎ Custom", "✎ Özelleştir"), editModelBtn, 9.5, Theme.Text, true, HAlign.Center);
        isl.AddHit("ai:model_info", editModelBtn);

        // 5. Кнопка Готово внизу
        var doneBtn = new Rect(r.Left + (r.Width - 160) / 2.0, r.Bottom - 34, 160, 26);
        p.Card(doneBtn, Theme.Mix(Theme.Surface2, Theme.Accent, 0.45), 13, Theme.Accent);
        p.Label("✓ " + Loc.T("Готово", "Done", "Tamam"), doneBtn, 10.5, Colors.White, true, HAlign.Center);
        isl.AddHit("ai:toggle_settings", doneBtn);
    }

    // ==================== AERO CHAT HISTORY VIEW ====================
    private void PaintHistoryView(IslandView isl, Painter p, Rect r)
    {
        p.Card(r, Theme.Mix(Theme.Surface, Theme.Surface2, 0.45), 16, Theme.BorderSubtle);

        // Заголовок
        p.Label("🕒 " + Loc.T("История диалогов", "Chat History", "Sohbet Geçmişi"),
            new Rect(r.Left + 20, r.Top + 14, 200, 24), 14, Theme.Text, true);

        // Кнопка «+ Новый чат»
        string newChatText = "+ " + Loc.T("Новый чат", "New chat", "Yeni sohbet");
        double newBtnW = p.TextWidth(newChatText, 10, true) + 18;
        var newChatBtn = new Rect(r.Right - 80 - newBtnW, r.Top + 12, newBtnW, 26);
        newChatBtn = p.PressRect(isl, "ai:new_chat", newChatBtn, 1.5);
        p.Card(newChatBtn, Theme.Mix(Theme.Surface2, Theme.Accent, 0.45), 13, Theme.Accent);
        p.Label(newChatText, newChatBtn, 10, Colors.White, true, HAlign.Center);
        isl.AddHit("ai:new_chat", newChatBtn);

        // Кнопка Закрыть в правом верхнем углу
        var closeBtn = new Rect(r.Right - 74, r.Top + 12, 60, 26);
        var pressClose = p.PressRect(isl, "ai:toggle_history", closeBtn, 1.5);
        p.Card(pressClose, Theme.Mix(Theme.Surface2, Theme.Surface3, isl.Hv("ai:toggle_history")), 13, Theme.BorderSubtle);
        p.Label("✕ " + Loc.T("Назад", "Back", "Geri"), pressClose, 10, Theme.Text, true, HAlign.Center);
        isl.AddHit("ai:toggle_history", closeBtn);

        p.Line(new Point(r.Left + 16, r.Top + 44), new Point(r.Right - 16, r.Top + 44), Theme.BorderSubtle, 1.0);

        if (_sessions.Count == 0)
        {
            // Пустая история
            var emptyRect = new Rect(r.Left + 20, r.Top + 80, r.Width - 40, 140);
            p.Card(emptyRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.5), 14, Theme.BorderSubtle);
            p.Label("💬", new Rect(emptyRect.Left, emptyRect.Top + 24, emptyRect.Width, 28), 24, Theme.Muted, true, HAlign.Center);
            p.Label(Loc.T("История диалогов пуста", "Chat history is empty", "Sohbet geçmişi boş"),
                new Rect(emptyRect.Left, emptyRect.Top + 58, emptyRect.Width, 20), 12, Theme.Text, true, HAlign.Center);
            p.Label(Loc.T("Когда вы общаетесь с Аэро, ваши беседы сохраняются здесь.",
                          "When you chat with Aero, conversations will be saved here.",
                          "Aero ile konuştuğunuzda sohbetleriniz burada kaydedilir."),
                new Rect(emptyRect.Left, emptyRect.Top + 82, emptyRect.Width, 18), 10, Theme.Muted, false, HAlign.Center);
            return;
        }

        // Область списка сессий
        double listTop = r.Top + 52;
        double listBottom = r.Bottom - 44;
        double listH = listBottom - listTop;
        var listClip = new Rect(r.Left + 16, listTop, r.Width - 32, listH);

        double itemH = 54;
        double itemGap = 6;
        double step = itemH + itemGap;
        double totalH = _sessions.Count * step;
        double maxScroll = Math.Max(0.0, totalH - listH);

        var spScroll = isl.Nsp("ai_hist_scroll");
        spScroll.Target = Math.Clamp(spScroll.Target, 0.0, maxScroll);
        if (!spScroll.Settled) isl.StartFrames();
        double scroll = Math.Clamp(spScroll.X, 0.0, maxScroll);

        using (p.WithClip(new RectangleGeometry(listClip)))
        {
            double curY = listClip.Top - scroll;

            for (int i = 0; i < _sessions.Count; i++)
            {
                var s = _sessions[i];
                var itemRect = new Rect(listClip.Left, curY, listClip.Width, itemH);

                if (itemRect.Bottom >= listClip.Top - 10 && itemRect.Top <= listClip.Bottom + 10)
                {
                    bool isCurrent = s.Id == _currentSessionId;
                    string itemKey = $"ai:hist:load:{s.Id}";
                    double hv = isl.Hv(itemKey);

                    var itemPress = p.PressRect(isl, itemKey, itemRect, 1.5);
                    var itemBg = isCurrent
                        ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.35 + 0.15 * hv)
                        : Theme.Mix(Theme.Surface, Theme.Surface2, 0.5 + 0.5 * hv);
                    var itemBorder = isCurrent ? Theme.Accent : Theme.Mix(Theme.BorderSubtle, Theme.Accent, hv * 0.4);

                    p.Card(itemPress, itemBg, 12, itemBorder);
                    isl.AddHit(itemKey, itemRect);

                    // Иконка слева
                    string icon = isCurrent ? "✦" : "💬";
                    Color iconColor = isCurrent ? Theme.AccentHot : Theme.Muted;
                    p.Label(icon, new Rect(itemPress.Left + 12, itemPress.Top + 16, 20, 20), 13, iconColor, true, HAlign.Center);

                    // Текстовая информация (Заголовок и дата)
                    double textLeft = itemPress.Left + 38;
                    double textRight = itemPress.Right - 44;
                    double textW = Math.Max(20, textRight - textLeft);

                    p.Label(s.Title, new Rect(textLeft, itemPress.Top + 8, textW, 20), 12, isCurrent ? Colors.White : Theme.Text, true);

                    int msgCount = s.Messages.Count;
                    string countStr = Loc.T($"{msgCount} сообщ.", $"{msgCount} msgs", $"{msgCount} mesaj");
                    string dateStr = s.UpdatedAt.ToString("dd.MM HH:mm");
                    string metaStr = $"{countStr} · {dateStr}";
                    p.Label(metaStr, new Rect(textLeft, itemPress.Top + 28, textW, 16), 9.5, isCurrent ? Theme.Mix(Colors.White, Theme.Accent, 0.7) : Theme.Muted);

                    // Кнопка удаления 🗑 справа
                    string delKey = $"ai:hist:del:{s.Id}";
                    var delRect = new Rect(itemPress.Right - 36, itemPress.Top + 13, 28, 28);
                    delRect = p.PressRect(isl, delKey, delRect, 1.5);
                    double delHv = isl.Hv(delKey);
                    if (delHv > 0.01)
                    {
                        p.Card(delRect, Theme.Mix(Theme.Surface2, Theme.Red, 0.25 + 0.25 * delHv), 8, Theme.Red);
                    }
                    p.Label("🗑", delRect, 11, delHv > 0.01 ? Theme.Red : Theme.Muted, true, HAlign.Center);
                    isl.AddHit(delKey, delRect);
                }

                curY += step;
            }
        }

        // Тонкий скроллбар
        if (maxScroll > 1)
        {
            double scrollFrac = scroll / maxScroll;
            double trackH = listClip.Height;
            double thumbH = Math.Max(24, trackH * (trackH / totalH));
            double thumbY = listClip.Top + scrollFrac * (trackH - thumbH);
            p.Card(new Rect(listClip.Right - 4, thumbY, 3, thumbH), Color.FromArgb(85, 255, 255, 255), 1.5);
        }

        // Нижняя строка: Кнопка «Очистить всю историю»
        var clearAllBtn = new Rect(r.Left + 20, r.Bottom - 36, 175, 26);
        clearAllBtn = p.PressRect(isl, "ai:hist:clear_all", clearAllBtn, 1.5);
        double caHv = isl.Hv("ai:hist:clear_all");
        var caBg = Theme.Mix(Theme.Surface, Theme.Red, 0.12 + 0.15 * caHv);
        p.Card(clearAllBtn, caBg, 12, caHv > 0.01 ? Theme.Red : Theme.BorderSubtle);
        p.Label("🗑 " + Loc.T("Очистить всю историю", "Clear all history", "Tüm geçmişi sil"),
            clearAllBtn, 9.5, caHv > 0.01 ? Theme.Red : Theme.Muted, true, HAlign.Center);
        isl.AddHit("ai:hist:clear_all", clearAllBtn);
    }

    // ==================== PAINTING ====================
    public override void Paint(IslandView isl, Painter p, Rect area)
    {
        _codeSnippets.Clear();

        // ==================== 1. ВЕРХНИЙ БАР (HEADER) ====================
        double topY = area.Top + 2;

        // Заголовок раздела
        p.Label("🤖 " + Loc.T("Аэро", "Aero", "Aero"), new Rect(area.Left + 4, topY + 1, 90, 26), 16, Theme.Text, true);

        // Активный бейдж модели и режима (кликабельный, открывает настройки)
        string provIcon = isl.S.AiProvider switch { "openai" => "🟢 OpenAI", "deepseek" => "🐳 DeepSeek", _ => "✦ Gemini" };
        string modeLabel = isl.S.AiMode == "fast" ? "⚡ Fast" : "🧠 Pro";
        string infoText = $"{provIcon} · {modeLabel}";
        double infoW = p.TextWidth(infoText, 10, false) + 16;
        var infoRect = new Rect(area.Left + 98, topY + 2, infoW, 24);
        p.Card(infoRect, Theme.Mix(Theme.Surface, Theme.Surface2, 0.6 + 0.3 * isl.Hv("ai:toggle_settings")), 12, Theme.BorderSubtle);
        p.Label(infoText, infoRect, 10, Theme.Muted, false, HAlign.Center);
        isl.AddHit("ai:toggle_settings", infoRect);

        // Правые кнопки управления: Настройки (всегда), История (всегда), а также Очистить и Копировать (при сообщениях)
        string setLabel = Loc.T("⚙ Настройки", "⚙ Settings", "⚙ Ayarlar");
        double setBtnW = p.TextWidth(setLabel, 10, true) + 18;
        var setBtnRect = new Rect(area.Right - setBtnW, topY + 1, setBtnW, 26);
        setBtnRect = p.PressRect(isl, "ai:toggle_settings", setBtnRect, 1.5);
        var setBg = _showSettings ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.45) : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("ai:toggle_settings"));
        p.Card(setBtnRect, setBg, 13, _showSettings ? Theme.Accent : Theme.BorderSubtle);
        p.Label(setLabel, setBtnRect, 10, _showSettings ? Colors.White : Theme.Text, true, HAlign.Center);
        isl.AddHit("ai:toggle_settings", setBtnRect);

        double rightOffset = setBtnW + 8;

        // Кнопка истории диалогов
        string histLabel = Loc.T("🕒 История", "🕒 History", "🕒 Geçmiş");
        if (_sessions.Count > 0) histLabel += $" ({_sessions.Count})";
        double histBtnW = p.TextWidth(histLabel, 10, true) + 18;
        var histBtnRect = new Rect(area.Right - rightOffset - histBtnW, topY + 1, histBtnW, 26);
        histBtnRect = p.PressRect(isl, "ai:toggle_history", histBtnRect, 1.5);
        var histBg = _showHistory ? Theme.Mix(Theme.Surface2, Theme.Accent, 0.45) : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("ai:toggle_history"));
        p.Card(histBtnRect, histBg, 13, _showHistory ? Theme.Accent : Theme.BorderSubtle);
        p.Label(histLabel, histBtnRect, 10, _showHistory ? Colors.White : Theme.Text, true, HAlign.Center);
        isl.AddHit("ai:toggle_history", histBtnRect);
        rightOffset += histBtnW + 8;

        if (_messages.Count > 0 && !_showSettings && !_showHistory)
        {
            // Кнопка нового чата
            string clearLabel = Loc.T("✕ Новый чат", "✕ New chat", "✕ Yeni sohbet");
            double clearBtnW = p.TextWidth(clearLabel, 10, true) + 16;
            var clearBtnRect = new Rect(area.Right - rightOffset - clearBtnW, topY + 1, clearBtnW, 26);
            clearBtnRect = p.PressRect(isl, "ai:clear", clearBtnRect, 1.5);
            p.Card(clearBtnRect, Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("ai:clear")), 13, Theme.BorderSubtle);
            p.Label(clearLabel, clearBtnRect, 10, Theme.Muted, true, HAlign.Center);
            isl.AddHit("ai:clear", clearBtnRect);
            rightOffset += clearBtnW + 6;

            // Кнопка копирования последнего ответа
            var lastModelMsg = _messages.LastOrDefault(m => m.Role == "model" && !string.IsNullOrWhiteSpace(m.Text));
            if (lastModelMsg != null)
            {
                bool justCopied = Clock.Now < _copiedFeedbackTime && _copiedCodeIdx == -1;
                string copyLabel = justCopied ? Loc.T("✓ Скопировано", "✓ Copied", "✓ Kopyalandı") : Loc.T("📋 Копировать", "📋 Copy", "📋 Kopyala");
                double copyBtnW = p.TextWidth(copyLabel, 10, true) + 16;
                var copyBtnRect = new Rect(area.Right - rightOffset - copyBtnW, topY + 1, copyBtnW, 26);
                copyBtnRect = p.PressRect(isl, "ai:copy", copyBtnRect, 1.5);
                p.Card(copyBtnRect, justCopied ? Theme.Mix(Theme.Surface2, Theme.Green, 0.4) : Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("ai:copy")), 13, Theme.BorderSubtle);
                p.Label(copyLabel, copyBtnRect, 10, justCopied ? Theme.Green : Theme.Text, true, HAlign.Center);
                isl.AddHit("ai:copy", copyBtnRect);
            }
        }

        // ==================== 2. СЦЕНА РОБОТА АЭРО (СЛЕВА) ====================
        double contentTop = topY + 36;
        double stageW = 180;
        var stageRect = new Rect(area.Left + 2, contentTop, stageW, area.Bottom - contentTop - 2);
        PaintFullBodyRobot(isl, p, stageRect);

        // ==================== 3. ПРАВАЯ ОБЛАСТЬ (НАСТРОЙКИ, ИСТОРИЯ ИЛИ ЧАТ) ====================
        double rightX = stageRect.Right + 12;
        double rightW = area.Right - rightX - 2;
        double rightH = area.Bottom - contentTop - 2;
        var rightCenter = new Point(rightX + rightW / 2.0, contentTop + rightH / 2.0);

        _settingsSpring.Target = _showSettings ? 1.0 : 0.0;
        _historySpring.Target = _showHistory ? 1.0 : 0.0;

        if (!_settingsSpring.Settled)
        {
            _settingsSpring.Step(0.016, 340, 26);
            isl.StartFrames();
        }
        if (!_historySpring.Settled)
        {
            _historySpring.Step(0.016, 340, 26);
            isl.StartFrames();
        }

        double setSpring = _settingsSpring.X;
        double histSpring = _historySpring.X;
        var subViewRect = new Rect(rightX, contentTop, rightW, rightH);

        if (setSpring > 0.99)
        {
            PaintSettingsView(isl, p, subViewRect);
            return;
        }
        if (histSpring > 0.99)
        {
            PaintHistoryView(isl, p, subViewRect);
            return;
        }

        if (setSpring > 0.01)
        {
            using (p.WithOpacity(setSpring))
            using (p.WithScale(0.96 + 0.04 * setSpring, rightCenter))
            {
                PaintSettingsView(isl, p, subViewRect);
            }
        }
        if (histSpring > 0.01)
        {
            using (p.WithOpacity(histSpring))
            using (p.WithScale(0.96 + 0.04 * histSpring, rightCenter))
            {
                PaintHistoryView(isl, p, subViewRect);
            }
        }

        double overlayA = Math.Max(setSpring, histSpring);
        if (overlayA > 0.8) return;

        using var chatScope = overlayA > 0.01 ? p.WithOpacity(1.0 - overlayA) : Scope.Empty;
        using var chatScale = overlayA > 0.01 ? p.WithScale(1.0 - 0.04 * overlayA, rightCenter) : Scope.Empty;

        // Нижняя строка ввода
        double barH = 46;
        var inputBarRect = new Rect(rightX, area.Bottom - barH - 2, rightW, barH);
        var barBg = Theme.Mix(Theme.Surface, Theme.Surface2, _isFocused ? 0.95 : 0.65);
        var barBorder = _isFocused ? Theme.Accent : Theme.BorderSubtle;
        p.Card(inputBarRect, barBg, 23, barBorder);
        isl.AddHit("ai:focus_input", inputBarRect);

        // Иконка Sparkle слева
        p.Label("✦", new Rect(inputBarRect.Left + 16, inputBarRect.Top + 12, 22, 22), 14, Theme.AccentHot);

        // Правые элементы (кнопка вставки или отправки)
        bool hasInput = !string.IsNullOrWhiteSpace(InputText);
        double rightBtnsW = 42;

        if (hasInput)
        {
            // Кнопка очистить ввод
            var clearInputRect = new Rect(inputBarRect.Right - 76, inputBarRect.Top + 9, 28, 28);
            var pressClear = p.PressRect(isl, "ai:clear_input", clearInputRect, 1.5);
            p.Card(pressClear, Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("ai:clear_input")), 14);
            p.Label("✕", pressClear, 10, Theme.Muted, true, HAlign.Center);
            isl.AddHit("ai:clear_input", clearInputRect);

            // Кнопка отправить
            var sendRect = new Rect(inputBarRect.Right - 42, inputBarRect.Top + 7, 32, 32);
            var pressSend = p.PressRect(isl, "ai:submit", sendRect, 2.0);
            p.Circle(new Point(pressSend.Left + pressSend.Width / 2.0, pressSend.Top + pressSend.Height / 2.0), pressSend.Width / 2.0, Theme.Accent);
            p.Label("➔", pressSend, 12, Colors.White, true, HAlign.Center);
            isl.AddHit("ai:submit", sendRect);
            rightBtnsW = 82;
        }
        else
        {
            // Быстрая кнопка «Вставить»
            string pasteText = Loc.T("📋 Вставить", "📋 Paste", "📋 Yapıştır");
            double pasteW = p.TextWidth(pasteText, 10, true) + 16;
            var pasteRect = new Rect(inputBarRect.Right - pasteW - 12, inputBarRect.Top + 10, pasteW, 26);
            var pressPaste = p.PressRect(isl, "ai:act:paste", pasteRect, 1.5);
            p.Card(pressPaste, Theme.Mix(Theme.Surface, Theme.Surface2, isl.Hv("ai:act:paste")), 13, Theme.BorderSubtle);
            p.Label(pasteText, pressPaste, 10, Theme.Muted, true, HAlign.Center);
            isl.AddHit("ai:act:paste", pasteRect);
            rightBtnsW = pasteW + 20;
        }

        // Поле ввода текста
        double textLeft = inputBarRect.Left + 42;
        double textRight = inputBarRect.Right - rightBtnsW - 8;
        var textRect = new Rect(textLeft, inputBarRect.Top + 10, Math.Max(0, textRight - textLeft), 26);

        using (p.WithClip(new RectangleGeometry(textRect)))
        {
            if (string.IsNullOrEmpty(InputText))
            {
                string placeholder = Loc.T("Спросите что угодно у Аэро...", "Ask Aero anything...", "Aero'ya dilediğinizi sorun...");
                p.Label(placeholder, textRect, 12.5, Theme.Mix(Theme.Muted, Theme.Surface3, 0.7));
                if (_isFocused)
                {
                    p.Line(new Point(textRect.Left + 1, textRect.Top + 3), new Point(textRect.Left + 1, textRect.Bottom - 3), Theme.AccentHot, 1.8);
                }
            }
            else
            {
                double tw = p.TextWidth(InputText, 12.5, false);
                double offset = tw > textRect.Width - 10 ? (textRect.Width - 10) - tw : 0;
                p.Label(InputText, new Rect(textRect.Left + offset, textRect.Top, tw + 20, textRect.Height), 12.5, Theme.Text);
                if (_isFocused)
                {
                    double curX = textRect.Left + offset + tw + 2;
                    p.Line(new Point(curX, textRect.Top + 3), new Point(curX, textRect.Bottom - 3), Theme.AccentHot, 1.8);
                }
            }
        }

        // ==================== 4. ОБЛАСТЬ КОНТЕНТА (CONTENT AREA) ====================
        double contentH = inputBarRect.Top - contentTop - 10;
        var contentRect = new Rect(rightX, contentTop, rightW, contentH);

        bool isIdle = _messages.Count == 0 && !_loading && _error == null;

        if (isIdle)
        {
            // ---------- AERO WELCOME SCREEN ----------
            double cy = contentRect.Top + 8;

            // Приветственное речевое облачко Аэро с хвостиком влево
            var heroBubbleRect = new Rect(contentRect.Left + 10, cy, contentRect.Width - 20, 62);
            var heroBg = Theme.Mix(Theme.Surface, Theme.Surface2, 0.75);
            p.Card(heroBubbleRect, heroBg, 14, Theme.Mix(Theme.BorderSubtle, Theme.Accent, 0.35));

            p.Polygon(heroBg,
                new Point(heroBubbleRect.Left, heroBubbleRect.Top + 20),
                new Point(heroBubbleRect.Left - 8, heroBubbleRect.Top + 28),
                new Point(heroBubbleRect.Left, heroBubbleRect.Top + 36));

            p.Label(Loc.T("Привет! Я твой ИИ-помощник Аэро 🤖",
                          "Hello! I'm your AI assistant Aero 🤖",
                          "Merhaba! Ben yapay zeka asistanın Aero 🤖"),
                new Rect(heroBubbleRect.Left + 14, heroBubbleRect.Top + 10, heroBubbleRect.Width - 28, 20), 13.5, Theme.Text, true);

            string modelDesc = isl.S.GetEffectiveAiModel();
            p.Label(Loc.T($"Спроси меня о чём угодно ({modelDesc}) или выбери быстрое действие:",
                          $"Ask me anything ({modelDesc}) or choose a quick action below:",
                          $"Bana dilediğini sor ({modelDesc}) veya hızlı bir eylem seç:"),
                new Rect(heroBubbleRect.Left + 14, heroBubbleRect.Top + 32, heroBubbleRect.Width - 28, 18), 10.5, Theme.Muted);

            cy += 72;

            // Сетка карточек подсказок 2x2
            var promptCards = new[]
            {
                ("ai:act:summarize", "📝", Loc.T("Сделай выжимку", "Summarize", "Özet çıkar"),
                 Loc.T("Кратко обобщить текст из буфера", "Key points from clipboard", "Panodaki metni özetle")),

                ("ai:act:code", "💻", Loc.T("Разбор кода", "Code Review", "Kod İnceleme"),
                 Loc.T("Найти ошибки и исправить код", "Find bugs and fix code", "Hataları bul ve düzelt")),

                ("ai:act:translate", "🌐", Loc.T("Переводчик", "Translate", "Çeviri"),
                 Loc.T("Перевод ru / en / tr", "Fluent translation ru/en/tr", "Akıcı çeviri ru/en/tr")),

                ("ai:act:rewrite", "✍️", Loc.T("Рерайт и стиль", "Polish text", "Yeniden yaz"),
                 Loc.T("Сделать текст выразительным", "Improve clarity and tone", "Metni etkili hale getir"))
            };

            double gridW = contentRect.Width - 20;
            double cardW = (gridW - 10) / 2.0;
            double cardH = 68;

            for (int i = 0; i < promptCards.Length; i++)
            {
                int row = i / 2;
                int col = i % 2;
                double cardX = contentRect.Left + 10 + col * (cardW + 10);
                double cardY = cy + row * (cardH + 10);
                var cardRect = new Rect(cardX, cardY, cardW, cardH);

                var (actKey, icon, title, subtitle) = promptCards[i];
                double hv = isl.Hv(actKey);
                cardRect = p.PressRect(isl, actKey, cardRect, 2.0);

                var cardBg = Theme.Mix(Theme.Surface, Theme.Surface2, 0.6 + 0.4 * hv);
                var cardBorder = Theme.Mix(Theme.BorderSubtle, Theme.Accent, hv * 0.7);
                p.Card(cardRect, cardBg, 12, cardBorder);

                p.Label(icon, new Rect(cardRect.Left + 12, cardRect.Top + 14, 24, 24), 15, Theme.AccentHot);
                p.Label(title, new Rect(cardRect.Left + 42, cardRect.Top + 12, cardRect.Width - 48, 18), 11.5, Theme.Text, true);
                p.Label(subtitle, new Rect(cardRect.Left + 42, cardRect.Top + 34, cardRect.Width - 48, 16), 10, Theme.Muted);

                isl.AddHit(actKey, cardRect);
            }

            bool hasKey = !string.IsNullOrWhiteSpace(AiService.GetActiveKey(isl.S));
            if (!hasKey)
            {
                double tipY = cy + 2 * (cardH + 10) + 8;
                var tipRect = new Rect(contentRect.Left + 10, tipY, contentRect.Width - 20, 32);
                p.Card(tipRect, Theme.Mix(Theme.Surface, Theme.Red, 0.15), 10, Theme.Mix(Theme.BorderSubtle, Theme.Red, 0.4));
                p.Label(Loc.T("🔑 Нужен API-ключ. Нажмите здесь или в «⚙ Настройки», чтобы настроить.",
                              "🔑 API key required. Click here or '⚙ Settings' to configure.",
                              "🔑 API anahtarı gerekli. Yapılandırmak için buraya veya '⚙ Ayarlar'a tıklayın."),
                    tipRect, 10, Theme.Red, false, HAlign.Center);
                isl.AddHit("ai:toggle_settings", tipRect);
            }
        }
        else
        {
            // ---------- CHAT STREAMING VIEW ----------
            p.Card(contentRect, Theme.Surface, 16, Theme.BorderSubtle);

            var clipRect = new Rect(contentRect.Left + 14, contentRect.Top + 12, contentRect.Width - 28, contentRect.Height - 24);

            // Сначала рассчитываем полную высоту контента
            double totalContentHeight = MeasureChatHeight(p, clipRect.Width);
            _maxScroll = Math.Max(0, totalContentHeight - clipRect.Height);

            var spScroll = isl.Nsp("ai_chat_scroll");
            if (_autoScrollToBottom)
            {
                spScroll.Target = _maxScroll;
            }
            else
            {
                spScroll.Target = Math.Clamp(spScroll.Target, 0, _maxScroll);
            }

            if (!spScroll.Settled) isl.StartFrames();

            double scroll = Math.Clamp(spScroll.X, 0, _maxScroll);

            using (p.WithClip(new RectangleGeometry(clipRect)))
            {
                double curY = clipRect.Top - scroll;

                for (int msgIdx = 0; msgIdx < _messages.Count; msgIdx++)
                {
                    var msg = _messages[msgIdx];
                    double age = Clock.Now - msg.CreatedAt;
                    double entry = Math.Clamp(age / 0.20, 0.0, 1.0);
                    if (entry < 1.0) isl.StartFrames();

                    if (msg.Role == "user")
                    {
                        // Бабл сообщения пользователя (справа)
                        double maxBubbleW = Math.Min(clipRect.Width - 60, 580);
                        double tw = p.TextWidth(msg.Text, 12, false);
                        double bubbleW = Math.Min(maxBubbleW, Math.Max(90, tw + 28));
                        double textH = p.WrappedHeight(msg.Text, 12, bubbleW - 24);
                        double bubbleH = Math.Max(38, textH + 20);

                        // Отрисовываем бабл только если он попадает в видимую область скролла
                        if (curY + bubbleH >= clipRect.Top - 20 && curY <= clipRect.Bottom + 20)
                        {
                            var bubbleRect = new Rect(clipRect.Right - bubbleW, curY, bubbleW, bubbleH);
                            using (p.WithOpacity(entry))
                            using (p.WithScale(0.96 + 0.04 * entry, new Point(bubbleRect.Right - 15, bubbleRect.Top + bubbleH / 2.0)))
                            {
                                p.Card(bubbleRect, Theme.Mix(Theme.Surface2, Theme.Accent, 0.28), 16, Theme.BorderSubtle);

                                p.Label(msg.Text, new Rect(bubbleRect.Left + 12, bubbleRect.Top + 10, bubbleRect.Width - 24, textH),
                                    12, Colors.White, false, HAlign.Left, VAlign.Top, true);
                            }
                        }

                        curY += bubbleH + 16;
                    }
                    else
                    {
                        // Ответ Аэро (слева, реплика робота)
                        using (p.WithOpacity(entry))
                        {
                            if (curY + 30 >= clipRect.Top - 20 && curY <= clipRect.Bottom + 20)
                            {
                                var modelHeaderRect = new Rect(clipRect.Left + 8, curY, 200, 20);
                                p.Label("🤖 Аэро", modelHeaderRect, 11.5, Theme.AccentHot, true);
                                string timeStr = msg.Time.ToString("HH:mm");
                                p.Label(timeStr, new Rect(clipRect.Left + 66, curY + 1, 60, 18), 9.5, Theme.Muted);

                                // Хвостик реплики к роботу слева
                                p.Polygon(Theme.Mix(Theme.Surface2, Theme.Surface3, 0.45),
                                    new Point(clipRect.Left + 2, curY + 8),
                                    new Point(clipRect.Left - 6, curY + 13),
                                    new Point(clipRect.Left + 2, curY + 18));
                            }

                            curY += 24;

                            if (string.IsNullOrEmpty(msg.Text) && _loading)
                            {
                                // Индикатор размышления
                                if (curY + 28 >= clipRect.Top - 20 && curY <= clipRect.Bottom + 20)
                                {
                                    double ts = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                                    byte dotAlpha = (byte)(150 + 105 * Math.Sin(ts * 5.0));
                                    var dotColor = Color.FromArgb(dotAlpha, Theme.AccentHot.R, Theme.AccentHot.G, Theme.AccentHot.B);
                                    p.Circle(new Point(clipRect.Left + 8, curY + 8), 4, dotColor);
                                    p.Label(Loc.T("Аэро думает над ответом…", "Aero is thinking…", "Aero düşünüyor…"),
                                        new Rect(clipRect.Left + 20, curY, 260, 18), 11, Theme.AccentHot, true);
                                }
                                curY += 28;
                            }
                            else
                            {
                                var segments = GetSegments(msg);
                                double modelContentW = clipRect.Width - 16;

                                foreach (var seg in segments)
                                {
                                    if (seg.IsCode)
                                    {
                                        // Блок кода
                                        int snippetIdx = _codeSnippets.Count;
                                        _codeSnippets.Add(seg.Content);

                                        double codeW = modelContentW;
                                        double codeTextH = p.CodeWrappedHeight(seg.Content, 11, codeW - 24);
                                        double codeBoxH = 30 + Math.Max(24, codeTextH) + 12;

                                        if (curY + codeBoxH >= clipRect.Top - 20 && curY <= clipRect.Bottom + 20)
                                        {
                                            var codeBoxRect = new Rect(clipRect.Left, curY, codeW, codeBoxH);
                                            p.Card(codeBoxRect, Theme.Mix(Theme.Surface, Colors.Black, 0.45), 10, Theme.BorderSubtle);

                                            // Заголовок блока кода
                                            string langLabel = string.IsNullOrWhiteSpace(seg.Language) ? "CODE" : seg.Language.ToUpperInvariant();
                                            p.Label(langLabel, new Rect(codeBoxRect.Left + 12, codeBoxRect.Top + 6, 120, 18), 9.5, Theme.AccentHot, true);

                                            // Кнопка копирования сниппета
                                            bool isSnippetCopied = Clock.Now < _copiedFeedbackTime && _copiedCodeIdx == snippetIdx;
                                            string copyCodeText = isSnippetCopied ? Loc.T("✓ Скопировано", "✓ Copied", "✓ Kopyalandı") : Loc.T("📋 Копировать код", "📋 Copy code", "📋 Kodu kopyala");
                                            double btnW = p.TextWidth(copyCodeText, 9.5, true) + 14;
                                            var btnRect = new Rect(codeBoxRect.Right - btnW - 8, codeBoxRect.Top + 4, btnW, 22);

                                            string btnKey = $"ai:copy_code:{snippetIdx}";
                                            var pressBtn = p.PressRect(isl, btnKey, btnRect, 1.5);
                                            p.Card(pressBtn, Theme.Mix(Theme.Surface2, Theme.Surface3, isl.Hv(btnKey)), 8);
                                            p.Label(copyCodeText, pressBtn, 9.5, isSnippetCopied ? Theme.Green : Theme.Muted, true, HAlign.Center);
                                            isl.AddHit(btnKey, btnRect);

                                            // Разделительная линия
                                            p.Line(new Point(codeBoxRect.Left, codeBoxRect.Top + 28), new Point(codeBoxRect.Right, codeBoxRect.Top + 28), Theme.BorderSubtle, 1);

                                            // Сам код моноширинным шрифтом
                                            var codeTextRect = new Rect(codeBoxRect.Left + 12, codeBoxRect.Top + 34, codeW - 24, codeTextH);
                                            p.CodeLabel(seg.Content, codeTextRect, 11, Theme.Text);
                                        }

                                        curY += codeBoxH + 12;
                                    }
                                    else
                                    {
                                        // Обычный текст
                                        double textH = p.WrappedHeight(seg.Content, 12, modelContentW);
                                        if (curY + textH >= clipRect.Top - 20 && curY <= clipRect.Bottom + 20)
                                        {
                                            var labelRect = new Rect(clipRect.Left, curY, modelContentW, textH);
                                            p.Label(seg.Content, labelRect, 12, Theme.Text, false, HAlign.Left, VAlign.Top, true);
                                        }
                                        curY += textH + 12;
                                    }
                                }

                                curY += 12;
                            }
                        }
                    }
                }

                // Индикатор генерации следующего чанка
                if (_loading && _messages.Count > 0 && _messages.Last().Role == "model" && !string.IsNullOrEmpty(_messages.Last().Text))
                {
                    double ts = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                    byte dotAlpha = (byte)(150 + 105 * Math.Sin(ts * 6.0));
                    var dotColor = Color.FromArgb(dotAlpha, Theme.AccentHot.R, Theme.AccentHot.G, Theme.AccentHot.B);
                    p.Circle(new Point(clipRect.Left + 8, curY + 6), 3.5, dotColor);
                    p.Label("...", new Rect(clipRect.Left + 18, curY, 60, 16), 11, Theme.AccentHot, true);
                }
            }

            // Тонкий Apple-style скроллбар справа
            if (_maxScroll > 1)
            {
                double scrollFrac = scroll / _maxScroll;
                double trackH = clipRect.Height;
                double thumbH = Math.Max(26, trackH * (trackH / totalContentHeight));
                double thumbY = clipRect.Top + scrollFrac * (trackH - thumbH);
                var thumbRect = new Rect(clipRect.Right + 5, thumbY, 4, thumbH);
                p.Card(thumbRect, Color.FromArgb(85, 255, 255, 255), 2);
            }
        }
    }

    private double MeasureChatHeight(Painter p, double width)
    {
        double h = 8;
        foreach (var msg in _messages)
        {
            if (msg.Role == "user")
            {
                double maxBubbleW = Math.Min(width - 60, 580);
                double tw = p.TextWidth(msg.Text, 12, false);
                double bubbleW = Math.Min(maxBubbleW, Math.Max(90, tw + 28));
                double textH = p.WrappedHeight(msg.Text, 12, bubbleW - 24);
                h += Math.Max(38, textH + 20) + 16;
            }
            else
            {
                h += 24; // заголовок модели
                if (string.IsNullOrEmpty(msg.Text) && _loading)
                {
                    h += 28;
                }
                else
                {
                    var segments = GetSegments(msg);
                    double modelContentW = width - 16;
                    foreach (var seg in segments)
                    {
                        if (seg.IsCode)
                        {
                            double codeTextH = p.CodeWrappedHeight(seg.Content, 11, modelContentW - 24);
                            h += 30 + Math.Max(24, codeTextH) + 12 + 12;
                        }
                        else
                        {
                            double textH = p.WrappedHeight(seg.Content, 12, modelContentW);
                            h += textH + 12;
                        }
                    }
                    h += 12;
                }
            }
        }
        return h + 20;
    }

    // ==================== INTERACTION & CLICKS ====================
    public override void Click(IslandView isl, string key)
    {
        if (key.StartsWith("ai:prov:"))
        {
            string prov = key[8..];
            isl.S.AiProvider = prov;
            isl.S.AiModel = isl.S.GetEffectiveAiModel();
            isl.SaveState();
            isl.Kick();
        }
        else if (key.StartsWith("ai:mode:"))
        {
            string mode = key[8..];
            isl.S.AiMode = mode;
            isl.S.AiModel = isl.S.GetEffectiveAiModel();
            isl.SaveState();
            isl.Kick();
        }
        else if (key == "ai:model_info")
        {
            string cur = isl.S.GetEffectiveAiModel();
            string title = Loc.T("Модель нейросети", "AI Model", "Yapay Zeka Modeli");
            string prompt = Loc.T("Идентификатор модели для " + isl.S.AiProvider.ToUpperInvariant() + ":",
                                  "Model identifier for " + isl.S.AiProvider.ToUpperInvariant() + ":",
                                  isl.S.AiProvider.ToUpperInvariant() + " için model belirteci:");
            string? entered = isl.Ask(title, prompt, cur);
            if (!string.IsNullOrWhiteSpace(entered))
            {
                isl.S.AiModel = entered.Trim();
                isl.SaveState();
                isl.Kick();
            }
        }
        else if (key == "ai:focus_input")
        {
            _isFocused = true;
            isl.Focus();
            isl.Kick();
        }
        else if (key == "ai:pet_bot")
        {
            isl.PetRobot();
            isl.Kick();
        }
        else if (key == "ai:toggle_settings")
        {
            if (!_showSettings) _showHistory = false;
            _showSettings = !_showSettings;
            isl.Kick();
        }
        else if (key == "ai:toggle_history")
        {
            if (!_showHistory) _showSettings = false;
            _showHistory = !_showHistory;
            isl.Kick();
        }
        else if (key == "ai:new_chat")
        {
            SaveCurrentSession();
            _currentSessionId = null;
            _messages.Clear();
            _codeSnippets.Clear();
            _error = null;
            _showHistory = false;
            _autoScrollToBottom = true;
            var spScroll = isl.Nsp("ai_chat_scroll");
            spScroll.X = 0;
            spScroll.Target = 0;
            _maxScroll = 0;
            isl.Kick();
        }
        else if (key.StartsWith("ai:hist:load:"))
        {
            string id = key[13..];
            SaveCurrentSession();
            var sess = _sessions.FirstOrDefault(x => x.Id == id);
            if (sess != null)
            {
                _currentSessionId = sess.Id;
                _messages.Clear();
                _messages.AddRange(sess.Messages);
                _codeSnippets.Clear();
                _error = null;
                _showHistory = false;
                _autoScrollToBottom = true;
                var spScroll = isl.Nsp("ai_chat_scroll");
                spScroll.X = 0;
                spScroll.Target = 0;
            }
            isl.Kick();
        }
        else if (key.StartsWith("ai:hist:del:"))
        {
            string id = key[12..];
            int idx = _sessions.FindIndex(x => x.Id == id);
            if (idx >= 0)
            {
                _sessions.RemoveAt(idx);
                StoreService.SaveAeroChats(_sessions);
                if (_currentSessionId == id)
                {
                    _currentSessionId = null;
                    _messages.Clear();
                    _codeSnippets.Clear();
                    _error = null;
                }
            }
            isl.Kick();
        }
        else if (key == "ai:hist:clear_all")
        {
            _sessions.Clear();
            StoreService.SaveAeroChats(_sessions);
            _currentSessionId = null;
            _messages.Clear();
            _codeSnippets.Clear();
            _error = null;
            isl.Kick();
        }
        else if (key == "ai:clear_key")
        {
            AiService.SetActiveKey(isl.S, "");
            isl.SaveState();
            isl.Kick();
        }
        else if (key == "ai:clear_input")
        {
            InputText = "";
            _isFocused = true;
            isl.Focus();
            isl.Kick();
        }
        else if (key == "ai:submit")
        {
            if (!string.IsNullOrWhiteSpace(InputText))
            {
                string q = InputText.Trim();
                InputText = "";
                ExecutePrompt(isl, q);
            }
        }
        else if (key == "ai:key")
        {
            string current = AiService.GetActiveKey(isl.S);
            string title = Loc.T($"Настройка API-ключа {isl.S.AiProvider.ToUpperInvariant()}", $"Configure {isl.S.AiProvider.ToUpperInvariant()} Key", $"{isl.S.AiProvider.ToUpperInvariant()} Anahtarını Yapılandır");
            string prompt = Loc.T("Вставьте ваш персональный API-ключ:", "Paste your personal API key:", "Kişisel API anahtarınızı yapıştırın:");
            string? entered = isl.Ask(title, prompt, current);
            if (entered != null)
            {
                AiService.SetActiveKey(isl.S, entered);
                isl.SaveState();
                isl.Kick();
            }
        }
        else if (key == "ai:copy")
        {
            var lastModelMsg = _messages.LastOrDefault(m => m.Role == "model" && !string.IsNullOrWhiteSpace(m.Text));
            if (lastModelMsg != null)
            {
                try
                {
                    Clipboard.SetText(lastModelMsg.Text);
                    _copiedFeedbackTime = Clock.Now + 2.0;
                    _copiedCodeIdx = -1;
                    isl.Kick();
                }
                catch { }
            }
        }
        else if (key.StartsWith("ai:copy_code:"))
        {
            if (int.TryParse(key[13..], out int sIdx) && sIdx >= 0 && sIdx < _codeSnippets.Count)
            {
                try
                {
                    Clipboard.SetText(_codeSnippets[sIdx]);
                    _copiedFeedbackTime = Clock.Now + 2.0;
                    _copiedCodeIdx = sIdx;
                    isl.Kick();
                }
                catch { }
            }
        }
        else if (key == "ai:clear")
        {
            SaveCurrentSession();
            _currentSessionId = null;
            _messages.Clear();
            _codeSnippets.Clear();
            _error = null;
            var spScroll = isl.Nsp("ai_chat_scroll");
            spScroll.X = 0;
            spScroll.Target = 0;
            _maxScroll = 0;
            _autoScrollToBottom = true;
            isl.Kick();
        }
        else if (key == "ai:act:paste")
        {
            string clip = GetClipboardText();
            if (string.IsNullOrWhiteSpace(clip))
            {
                isl.Alert(Loc.T("Буфер обмена пуст.", "Clipboard is empty.", "Pano boş."));
                return;
            }
            InputText = clip;
            _isFocused = true;
            isl.Focus();
            isl.Kick();
        }
        else if (key == "ai:act:summarize")
        {
            string text = !string.IsNullOrWhiteSpace(InputText) ? InputText : GetClipboardText();
            if (string.IsNullOrWhiteSpace(text))
            {
                isl.Alert(Loc.T("Сначала введите или скопируйте текст.", "Please enter or copy text first.", "Lütfen önce bir metin girin veya kopyalayın."));
                return;
            }
            InputText = "";
            ExecutePrompt(isl, Loc.T("Сделай краткую, структурированную выжимку текста (основные тезисы):\n\n",
                                     "Provide a concise, well-structured summary of the following text (key takeaways):\n\n",
                                     "Aşağıdaki metnin kısa ve düzenli bir özetini çıkar (ana noktalar):\n\n") + text);
        }
        else if (key == "ai:act:translate")
        {
            string text = !string.IsNullOrWhiteSpace(InputText) ? InputText : GetClipboardText();
            if (string.IsNullOrWhiteSpace(text))
            {
                isl.Alert(Loc.T("Сначала введите или скопируйте текст.", "Please enter or copy text first.", "Lütfen önce bir metin girin veya kopyalayın."));
                return;
            }
            InputText = "";
            ExecutePrompt(isl, Loc.T("Переведи следующий текст (если на русском — на качественный английский, если на английском или другом — на русский):\n\n",
                                     "Translate the following text (to high-quality English, or to Russian if already in English):\n\n",
                                     "Aşağıdaki metni Türkçeye çevir (zaten Türkçe ise akıcı bir İngilizceye çevir):\n\n") + text);
        }
        else if (key == "ai:act:code")
        {
            string text = !string.IsNullOrWhiteSpace(InputText) ? InputText : GetClipboardText();
            if (string.IsNullOrWhiteSpace(text))
            {
                isl.Alert(Loc.T("Сначала введите или скопируйте код / ошибку.", "Please enter or copy code / error first.", "Lütfen önce kodu veya hatayı girin ya da kopyalayın."));
                return;
            }
            InputText = "";
            ExecutePrompt(isl, Loc.T("Проанализируй следующий код или ошибку, кратко объясни суть проблемы и предложи исправленный вариант:\n\n",
                                     "Analyze the following code or error, briefly explain the root cause and provide a fixed version:\n\n",
                                     "Aşağıdaki kodu veya hatayı analiz et, sorunun nedenini kısaca açıkla ve düzeltilmiş halini sun:\n\n") + text);
        }
        else if (key == "ai:act:rewrite")
        {
            string text = !string.IsNullOrWhiteSpace(InputText) ? InputText : GetClipboardText();
            if (string.IsNullOrWhiteSpace(text))
            {
                isl.Alert(Loc.T("Сначала введите или скопируйте текст.", "Please enter or copy text first.", "Lütfen önce bir metin girin или kopyalayın."));
                return;
            }
            InputText = "";
            ExecutePrompt(isl, Loc.T("Перепиши следующий текст более грамотно, профессионально и выразительно:\n\n",
                                     "Rewrite the following text more clearly, professionally, and eloquently:\n\n",
                                     "Aşağıdaki metni daha anlaşılır, profesyonel ve etkileyici bir dille yeniden yaz:\n\n") + text);
        }
    }

    public void OnTextInput(IslandView isl, string text)
    {
        if (!_isFocused) return;
        if (string.IsNullOrEmpty(text)) return;
        if (text == "\r" || text == "\n") return;

        InputText += text;
        isl.Kick();
    }

    public void OnKeyDown(IslandView isl, KeyEventArgs e)
    {
        if (!_isFocused) return;

        if (e.Key == WKey.Back)
        {
            if (InputText.Length > 0)
            {
                InputText = InputText[..^1];
                isl.Kick();
            }
            e.Handled = true;
        }
        else if (e.Key == WKey.Enter)
        {
            if (!string.IsNullOrWhiteSpace(InputText))
            {
                string q = InputText.Trim();
                InputText = "";
                ExecutePrompt(isl, q);
            }
            e.Handled = true;
        }
        else if (e.Key == WKey.Escape)
        {
            _isFocused = false;
            isl.Kick();
            e.Handled = true;
        }
        else if (e.Key == WKey.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            string clip = GetClipboardText();
            if (!string.IsNullOrEmpty(clip))
            {
                InputText += clip;
                isl.Kick();
            }
            e.Handled = true;
        }
        else if (e.Key is WKey.A or WKey.Delete && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            InputText = "";
            isl.Kick();
            e.Handled = true;
        }
    }

    public override void Wheel(IslandView isl, double notches)
    {
        if (_showHistory)
        {
            var spHist = isl.Nsp("ai_hist_scroll");
            spHist.Target = Math.Clamp(spHist.Target - notches * 54, 0, 999999);
            isl.StartFrames();
            isl.Kick();
            return;
        }

        _autoScrollToBottom = false;
        var spScroll = isl.Nsp("ai_chat_scroll");
        spScroll.Target = Math.Clamp(spScroll.Target - notches * 58, 0, _maxScroll);
        if (spScroll.Target >= _maxScroll - 4) _autoScrollToBottom = true;
        isl.StartFrames();
        isl.Kick();
    }

    private void ExecutePrompt(IslandView isl, string prompt)
    {
        _messages.Add(new ChatMessage { Role = "user", Text = prompt, Time = DateTime.Now, CreatedAt = Clock.Now });
        var modelMsg = new ChatMessage { Role = "model", Text = "", Time = DateTime.Now, CreatedAt = Clock.Now };
        _messages.Add(modelMsg);

        SaveCurrentSession();

        _loading = true;
        _error = null;
        _autoScrollToBottom = true;
        isl.StartFrames();
        isl.Kick();

        var historySnapshot = _messages.Take(Math.Max(0, _messages.Count - 2)).ToList();
        long lastKick = 0;

        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            var res = await AiService.AskStreamAsync(prompt, isl.S, partial =>
            {
                isl.Dispatcher.InvokeAsync(() =>
                {
                    modelMsg.Text = partial;
                    _talkingUntil = Clock.Now + 0.6;
                    long now = Stopwatch.GetTimestamp();
                    if ((now - lastKick) / (double)Stopwatch.Frequency >= 0.02)
                    {
                        lastKick = now;
                        isl.StartFrames();
                        isl.Kick();
                    }
                });
            }, historySnapshot);

            isl.Dispatcher.Invoke(() =>
            {
                _loading = false;
                _talkingUntil = Clock.Now + 0.4;
                if (res.Success)
                {
                    modelMsg.Text = res.Text;
                    _error = null;
                }
                else
                {
                    if (string.IsNullOrWhiteSpace(modelMsg.Text))
                    {
                        modelMsg.Text = $"⚠ {res.Error}";
                    }
                    _error = res.Error;
                }
                SaveCurrentSession();
                isl.StartFrames();
                isl.Kick();
            });
        });
    }

    private static string GetClipboardText()
    {
        try
        {
            if (Clipboard.ContainsText()) return Clipboard.GetText();
        }
        catch { }
        return "";
    }
}
