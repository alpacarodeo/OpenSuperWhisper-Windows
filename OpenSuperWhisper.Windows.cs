using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace OpenSuperWhisperWindows
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            string[] arguments = Environment.GetCommandLineArgs();
            const string ValidatePrefix = "--validate-hotkey=";
            const string ValidateLanguagePrefix = "--validate-language=";
            const string ExpectConfiguredPrefix = "--expect-configured-hotkey=";
            const string ExpectConfiguredLanguagePrefix = "--expect-configured-language=";
            foreach (string argument in arguments)
            {
                if (argument.StartsWith(ValidatePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    HotkeyDefinition definition;
                    string error;
                    Environment.ExitCode = HotkeyDefinition.TryParse(
                        argument.Substring(ValidatePrefix.Length),
                        out definition,
                        out error) ? 0 : 2;
                    return;
                }

                if (argument.StartsWith(ValidateLanguagePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageDefinition language;
                    string error;
                    Environment.ExitCode = LanguageDefinition.TryParse(
                        argument.Substring(ValidateLanguagePrefix.Length),
                        out language,
                        out error) ? 0 : 2;
                    return;
                }

                if (argument.StartsWith(ExpectConfiguredPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    HotkeyDefinition configured = HotkeyDefinition.Load();
                    Environment.ExitCode = string.Equals(
                        configured.DisplayName,
                        argument.Substring(ExpectConfiguredPrefix.Length),
                        StringComparison.OrdinalIgnoreCase) ? 0 : 3;
                    return;
                }

                if (argument.StartsWith(ExpectConfiguredLanguagePrefix, StringComparison.OrdinalIgnoreCase))
                {
                    LanguageDefinition configuredLanguage = LanguageDefinition.Load();
                    Environment.ExitCode = string.Equals(
                        configuredLanguage.Code,
                        argument.Substring(ExpectConfiguredLanguagePrefix.Length),
                        StringComparison.OrdinalIgnoreCase) ? 0 : 3;
                    return;
                }
            }

            bool createdNew;
            using (Mutex singleInstance = new Mutex(true, "Local\\OpenSuperWhisper.Windows", out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show(
                        "OpenSuperWhisper is already running in the notification area.",
                        "OpenSuperWhisper",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                    return;
                }

                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs eventArgs)
                {
                    AppLog.Write("UI exception: " + eventArgs.Exception);
                };
                AppDomain.CurrentDomain.UnhandledException += delegate(object sender, UnhandledExceptionEventArgs eventArgs)
                {
                    AppLog.Write("Unhandled exception: " + eventArgs.ExceptionObject);
                };

                try
                {
                    AppLog.Write("Application starting.");
                    Application.EnableVisualStyles();
                    Application.SetCompatibleTextRenderingDefault(false);
                    bool startInBackground = Array.Exists(
                        Environment.GetCommandLineArgs(),
                        delegate(string argument)
                        {
                            return string.Equals(argument, "--background", StringComparison.OrdinalIgnoreCase);
                        });
                    Application.Run(new MainForm(startInBackground));
                }
                catch (Exception exception)
                {
                    AppLog.Write("Startup failed: " + exception);
                    MessageBox.Show(
                        exception.Message,
                        "OpenSuperWhisper could not start",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error);
                }
            }
        }
    }

    internal static class AppLog
    {
        private static readonly string LogDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "OpenSuperWhisper");

        internal static readonly string LogPath = Path.Combine(LogDirectory, "OpenSuperWhisper.log");

        internal static void Write(string message)
        {
            try
            {
                Directory.CreateDirectory(LogDirectory);
                File.AppendAllText(
                    LogPath,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch
            {
                // Logging must never prevent dictation from working.
            }
        }
    }

    internal sealed class HotkeyDefinition
    {
        internal const uint ModAlt = 0x0001;
        internal const uint ModControl = 0x0002;
        internal const uint ModShift = 0x0004;
        internal const uint ModWin = 0x0008;
        internal const uint ModNoRepeat = 0x4000;
        internal const string DefaultText = "Shift+|";

        internal readonly uint Modifiers;
        internal readonly uint VirtualKey;
        internal readonly string DisplayName;

        private HotkeyDefinition(uint modifiers, uint virtualKey, string displayName)
        {
            Modifiers = modifiers;
            VirtualKey = virtualKey;
            DisplayName = displayName;
        }

        internal static string ConfigurationPath
        {
            get
            {
                string overrideDirectory = Environment.GetEnvironmentVariable("OPENSUPERWHISPER_CONFIG_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDirectory))
                {
                    return Path.Combine(overrideDirectory, "hotkey.txt");
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenSuperWhisper",
                    "hotkey.txt");
            }
        }

        internal static HotkeyDefinition Load()
        {
            string configuredText = DefaultText;
            try
            {
                if (File.Exists(ConfigurationPath))
                {
                    configuredText = File.ReadAllText(ConfigurationPath, Encoding.UTF8).Trim();
                }
            }
            catch (Exception exception)
            {
                AppLog.Write("Could not read hotkey configuration: " + exception.Message);
            }

            HotkeyDefinition definition;
            string error;
            if (TryParse(configuredText, out definition, out error))
            {
                return definition;
            }

            AppLog.Write("Invalid hotkey configuration '" + configuredText + "': " + error + ". Using " + DefaultText + ".");
            TryParse(DefaultText, out definition, out error);
            return definition;
        }

        internal static bool TryParse(string text, out HotkeyDefinition definition, out string error)
        {
            definition = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "The shortcut cannot be empty";
                return false;
            }

            string[] parts = text.Trim().Split(new char[] { '+' }, StringSplitOptions.None);
            if (parts.Length < 2)
            {
                error = "Use at least one modifier, for example Ctrl+Alt+M";
                return false;
            }

            uint modifiers = 0;
            for (int index = 0; index < parts.Length - 1; index++)
            {
                string modifier = parts[index].Trim();
                uint value;
                string canonical;
                if (modifier.Equals("Ctrl", StringComparison.OrdinalIgnoreCase) ||
                    modifier.Equals("Control", StringComparison.OrdinalIgnoreCase))
                {
                    value = ModControl;
                    canonical = "Ctrl";
                }
                else if (modifier.Equals("Alt", StringComparison.OrdinalIgnoreCase))
                {
                    value = ModAlt;
                    canonical = "Alt";
                }
                else if (modifier.Equals("Shift", StringComparison.OrdinalIgnoreCase))
                {
                    value = ModShift;
                    canonical = "Shift";
                }
                else if (modifier.Equals("Win", StringComparison.OrdinalIgnoreCase) ||
                         modifier.Equals("Windows", StringComparison.OrdinalIgnoreCase))
                {
                    value = ModWin;
                    canonical = "Win";
                }
                else
                {
                    error = "Unsupported modifier '" + modifier + "'";
                    return false;
                }

                if ((modifiers & value) != 0)
                {
                    error = "Modifier '" + canonical + "' is repeated";
                    return false;
                }
                modifiers |= value;
            }

            uint virtualKey;
            string keyName;
            if (!TryParseKey(parts[parts.Length - 1].Trim(), out virtualKey, out keyName))
            {
                error = "Unsupported key '" + parts[parts.Length - 1].Trim() + "'";
                return false;
            }

            StringBuilder display = new StringBuilder();
            AppendModifier(display, modifiers, ModControl, "Ctrl");
            AppendModifier(display, modifiers, ModAlt, "Alt");
            AppendModifier(display, modifiers, ModShift, "Shift");
            AppendModifier(display, modifiers, ModWin, "Win");
            display.Append(keyName);
            definition = new HotkeyDefinition(modifiers, virtualKey, display.ToString());
            return true;
        }

        private static void AppendModifier(StringBuilder display, uint modifiers, uint value, string name)
        {
            if ((modifiers & value) != 0)
            {
                display.Append(name);
                display.Append('+');
            }
        }

        private static bool TryParseKey(string text, out uint virtualKey, out string keyName)
        {
            virtualKey = 0;
            keyName = null;
            string key = text.Trim();
            if (key.Length == 1)
            {
                char character = char.ToUpperInvariant(key[0]);
                if ((character >= 'A' && character <= 'Z') || (character >= '0' && character <= '9'))
                {
                    virtualKey = character;
                    keyName = character.ToString();
                    return true;
                }
            }

            int functionNumber;
            if (key.Length >= 2 && char.ToUpperInvariant(key[0]) == 'F' &&
                int.TryParse(key.Substring(1), out functionNumber) &&
                functionNumber >= 1 && functionNumber <= 24)
            {
                virtualKey = (uint)(0x70 + functionNumber - 1);
                keyName = "F" + functionNumber;
                return true;
            }

            switch (key.ToUpperInvariant())
            {
                case "|":
                case "PIPE":
                case "BACKSLASH": virtualKey = 0xDC; keyName = "|"; return true;
                case "SPACE": virtualKey = 0x20; keyName = "Space"; return true;
                case "TAB": virtualKey = 0x09; keyName = "Tab"; return true;
                case "ENTER": virtualKey = 0x0D; keyName = "Enter"; return true;
                case "ESC":
                case "ESCAPE": virtualKey = 0x1B; keyName = "Escape"; return true;
                case "UP": virtualKey = 0x26; keyName = "Up"; return true;
                case "DOWN": virtualKey = 0x28; keyName = "Down"; return true;
                case "LEFT": virtualKey = 0x25; keyName = "Left"; return true;
                case "RIGHT": virtualKey = 0x27; keyName = "Right"; return true;
                case "HOME": virtualKey = 0x24; keyName = "Home"; return true;
                case "END": virtualKey = 0x23; keyName = "End"; return true;
                case "PAGEUP": virtualKey = 0x21; keyName = "PageUp"; return true;
                case "PAGEDOWN": virtualKey = 0x22; keyName = "PageDown"; return true;
                case "INSERT": virtualKey = 0x2D; keyName = "Insert"; return true;
                case "DELETE": virtualKey = 0x2E; keyName = "Delete"; return true;
                case "BACKTICK": virtualKey = 0xC0; keyName = "Backtick"; return true;
                case "SEMICOLON": virtualKey = 0xBA; keyName = "Semicolon"; return true;
                case "PLUS":
                case "EQUALS": virtualKey = 0xBB; keyName = "Plus"; return true;
                case "COMMA": virtualKey = 0xBC; keyName = "Comma"; return true;
                case "MINUS": virtualKey = 0xBD; keyName = "Minus"; return true;
                case "PERIOD": virtualKey = 0xBE; keyName = "Period"; return true;
                case "SLASH": virtualKey = 0xBF; keyName = "Slash"; return true;
                case "LEFTBRACKET": virtualKey = 0xDB; keyName = "LeftBracket"; return true;
                case "RIGHTBRACKET": virtualKey = 0xDD; keyName = "RightBracket"; return true;
                case "QUOTE": virtualKey = 0xDE; keyName = "Quote"; return true;
                default: return false;
            }
        }
    }

    internal sealed class LanguageDefinition
    {
        internal const string DefaultCode = "en";

        // Language codes and names mirrored from whisper.cpp (g_lang) so invalid
        // settings are rejected locally instead of failing inside the engine.
        private static readonly Dictionary<string, string> LanguageNames =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "en", "English" }, { "zh", "Chinese" }, { "de", "German" },
            { "es", "Spanish" }, { "ru", "Russian" }, { "ko", "Korean" },
            { "fr", "French" }, { "ja", "Japanese" }, { "pt", "Portuguese" },
            { "tr", "Turkish" }, { "pl", "Polish" }, { "ca", "Catalan" },
            { "nl", "Dutch" }, { "ar", "Arabic" }, { "sv", "Swedish" },
            { "it", "Italian" }, { "id", "Indonesian" }, { "hi", "Hindi" },
            { "fi", "Finnish" }, { "vi", "Vietnamese" }, { "he", "Hebrew" },
            { "uk", "Ukrainian" }, { "el", "Greek" }, { "ms", "Malay" },
            { "cs", "Czech" }, { "ro", "Romanian" }, { "da", "Danish" },
            { "hu", "Hungarian" }, { "ta", "Tamil" }, { "no", "Norwegian" },
            { "th", "Thai" }, { "ur", "Urdu" }, { "hr", "Croatian" },
            { "bg", "Bulgarian" }, { "lt", "Lithuanian" }, { "la", "Latin" },
            { "mi", "Maori" }, { "ml", "Malayalam" }, { "cy", "Welsh" },
            { "sk", "Slovak" }, { "te", "Telugu" }, { "fa", "Persian" },
            { "lv", "Latvian" }, { "bn", "Bengali" }, { "sr", "Serbian" },
            { "az", "Azerbaijani" }, { "sl", "Slovenian" }, { "kn", "Kannada" },
            { "et", "Estonian" }, { "mk", "Macedonian" }, { "br", "Breton" },
            { "eu", "Basque" }, { "is", "Icelandic" }, { "hy", "Armenian" },
            { "ne", "Nepali" }, { "mn", "Mongolian" }, { "bs", "Bosnian" },
            { "kk", "Kazakh" }, { "sq", "Albanian" }, { "sw", "Swahili" },
            { "gl", "Galician" }, { "mr", "Marathi" }, { "pa", "Punjabi" },
            { "si", "Sinhala" }, { "km", "Khmer" }, { "sn", "Shona" },
            { "yo", "Yoruba" }, { "so", "Somali" }, { "af", "Afrikaans" },
            { "oc", "Occitan" }, { "ka", "Georgian" }, { "be", "Belarusian" },
            { "tg", "Tajik" }, { "sd", "Sindhi" }, { "gu", "Gujarati" },
            { "am", "Amharic" }, { "yi", "Yiddish" }, { "lo", "Lao" },
            { "uz", "Uzbek" }, { "fo", "Faroese" }, { "ht", "Haitian Creole" },
            { "ps", "Pashto" }, { "tk", "Turkmen" }, { "nn", "Nynorsk" },
            { "mt", "Maltese" }, { "sa", "Sanskrit" }, { "lb", "Luxembourgish" },
            { "my", "Myanmar" }, { "bo", "Tibetan" }, { "tl", "Tagalog" },
            { "mg", "Malagasy" }, { "as", "Assamese" }, { "tt", "Tatar" },
            { "haw", "Hawaiian" }, { "ln", "Lingala" }, { "ha", "Hausa" },
            { "ba", "Bashkir" }, { "jw", "Javanese" }, { "su", "Sundanese" },
            { "yue", "Cantonese" }
        };

        private LanguageDefinition(string code, string displayName)
        {
            Code = code;
            DisplayName = displayName;
        }

        internal string Code { get; private set; }
        internal string DisplayName { get; private set; }

        internal static string ConfigurationPath
        {
            get
            {
                string overrideDirectory = Environment.GetEnvironmentVariable("OPENSUPERWHISPER_CONFIG_DIR");
                if (!string.IsNullOrWhiteSpace(overrideDirectory))
                {
                    return Path.Combine(overrideDirectory, "language.txt");
                }

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "OpenSuperWhisper",
                    "language.txt");
            }
        }

        internal static LanguageDefinition Load()
        {
            string configuredText = DefaultCode;
            try
            {
                if (File.Exists(ConfigurationPath))
                {
                    configuredText = File.ReadAllText(ConfigurationPath, Encoding.UTF8).Trim();
                }
            }
            catch (Exception exception)
            {
                AppLog.Write("Could not read language configuration: " + exception.Message);
            }

            LanguageDefinition definition;
            string error;
            if (TryParse(configuredText, out definition, out error))
            {
                return definition;
            }

            AppLog.Write("Invalid language configuration '" + configuredText + "': " + error + ". Using " + DefaultCode + ".");
            TryParse(DefaultCode, out definition, out error);
            return definition;
        }

        internal static bool TryParse(string text, out LanguageDefinition definition, out string error)
        {
            definition = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                error = "The language cannot be empty";
                return false;
            }

            string candidate = text.Trim();
            if (candidate.Equals("auto", StringComparison.OrdinalIgnoreCase) ||
                candidate.Equals("automatic", StringComparison.OrdinalIgnoreCase))
            {
                definition = new LanguageDefinition("auto", "Auto-detect");
                return true;
            }

            foreach (KeyValuePair<string, string> entry in LanguageNames)
            {
                if (entry.Key.Equals(candidate, StringComparison.OrdinalIgnoreCase) ||
                    entry.Value.Equals(candidate, StringComparison.OrdinalIgnoreCase))
                {
                    definition = new LanguageDefinition(entry.Key.ToLowerInvariant(), entry.Value);
                    return true;
                }
            }

            error = "Unsupported language '" + candidate + "'. Use a code such as en or de, a name such as English, or auto.";
            return false;
        }
    }

    // Keeps a whisper-server process running with the model permanently loaded
    // (in VRAM on CUDA builds) so each dictation skips the model-load step.
    // Every failure degrades silently to the per-dictation whisper-cli path.
    internal sealed class WhisperServer
    {
        private readonly object gate = new object();
        private readonly string serverPath;
        private readonly string modelPath;
        private readonly LanguageDefinition language;
        private readonly int threadCount;

        private Process process;
        private int port;
        private volatile bool ready;

        internal WhisperServer(string serverPath, string modelPath, LanguageDefinition language, int threadCount)
        {
            this.serverPath = serverPath;
            this.modelPath = modelPath;
            this.language = language;
            this.threadCount = threadCount;
        }

        internal bool IsAvailable
        {
            get
            {
                Process current = process;
                return ready && current != null && !current.HasExited;
            }
        }

        internal void StartInBackground()
        {
            Task.Run(delegate { StartAndWaitForReady(); });
        }

        internal string Transcribe(string audioPath)
        {
            if (!IsAvailable)
            {
                return null;
            }

            try
            {
                return PostInference(audioPath);
            }
            catch (Exception exception)
            {
                AppLog.Write("whisper-server transcription failed; falling back to whisper-cli. " + exception.Message);
                ready = false;
                return null;
            }
        }

        internal void Shutdown()
        {
            lock (gate)
            {
                ready = false;
                StopProcess();
            }
        }

        private void StartAndWaitForReady()
        {
            try
            {
                lock (gate)
                {
                    if (ready)
                    {
                        return;
                    }

                    StopProcess();
                    port = SelectFreePort();
                    process = StartProcess(port);
                }

                if (WaitForReady(TimeSpan.FromSeconds(180)))
                {
                    ready = true;
                    AppLog.Write("whisper-server ready on port " + port + ". The model stays loaded between dictations.");
                }
                else
                {
                    AppLog.Write("whisper-server did not become ready; dictations will use whisper-cli.");
                    Shutdown();
                }
            }
            catch (Exception exception)
            {
                AppLog.Write("whisper-server could not start; dictations will use whisper-cli. " + exception.Message);
                Shutdown();
            }
        }

        private static int SelectFreePort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int selectedPort = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return selectedPort;
        }

        private Process StartProcess(int selectedPort)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = serverPath;
            startInfo.Arguments =
                "-m \"" + modelPath + "\" " +
                "-l " + language.Code + " " +
                "-t " + threadCount + " " +
                "--host 127.0.0.1 --port " + selectedPort;
            startInfo.WorkingDirectory = Path.GetDirectoryName(serverPath);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            Process newProcess = Process.Start(startInfo);
            newProcess.OutputDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs.Data != null)
                {
                    AppLog.Write("[server] " + eventArgs.Data);
                }
            };
            newProcess.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs eventArgs)
            {
                if (eventArgs.Data != null)
                {
                    AppLog.Write("[server] " + eventArgs.Data);
                }
            };
            newProcess.BeginOutputReadLine();
            newProcess.BeginErrorReadLine();
            AppLog.Write("whisper-server starting on port " + selectedPort + " (language " + language.Code + ").");
            return newProcess;
        }

        private bool WaitForReady(TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                Process current = process;
                if (current == null || current.HasExited)
                {
                    return false;
                }

                if (ProbeHealth())
                {
                    return true;
                }

                Thread.Sleep(400);
            }

            return false;
        }

        private bool ProbeHealth()
        {
            try
            {
                HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/health");
                request.Method = "GET";
                request.Timeout = 2000;
                request.ReadWriteTimeout = 2000;
                using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
                {
                    return response.StatusCode == HttpStatusCode.OK;
                }
            }
            catch
            {
                return false;
            }
        }

        private string PostInference(string audioPath)
        {
            string boundary = "----OpenSuperWhisper" + Guid.NewGuid().ToString("N");
            StringBuilder fields = new StringBuilder();
            AppendFormField(fields, boundary, "response_format", "text");
            AppendFormField(fields, boundary, "language", language.Code);

            string fileHeader =
                "--" + boundary + "\r\n" +
                "Content-Disposition: form-data; name=\"file\"; filename=\"" +
                Path.GetFileName(audioPath) + "\"\r\n" +
                "Content-Type: audio/wav\r\n\r\n";

            byte[] headBytes = Encoding.UTF8.GetBytes(fields.ToString() + fileHeader);
            byte[] fileBytes = File.ReadAllBytes(audioPath);
            byte[] tailBytes = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");

            HttpWebRequest request = (HttpWebRequest)WebRequest.Create("http://127.0.0.1:" + port + "/inference");
            request.Method = "POST";
            request.ContentType = "multipart/form-data; boundary=" + boundary;
            request.Timeout = 600000;
            request.ReadWriteTimeout = 600000;
            request.ContentLength = headBytes.LongLength + fileBytes.LongLength + tailBytes.LongLength;

            using (Stream requestStream = request.GetRequestStream())
            {
                requestStream.Write(headBytes, 0, headBytes.Length);
                requestStream.Write(fileBytes, 0, fileBytes.Length);
                requestStream.Write(tailBytes, 0, tailBytes.Length);
            }

            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (StreamReader reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
            {
                return reader.ReadToEnd();
            }
        }

        private static void AppendFormField(StringBuilder builder, string boundary, string name, string value)
        {
            builder.Append("--" + boundary + "\r\n");
            builder.Append("Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n");
            builder.Append(value);
            builder.Append("\r\n");
        }

        private void StopProcess()
        {
            Process current = process;
            process = null;
            if (current == null)
            {
                return;
            }

            try
            {
                if (!current.HasExited)
                {
                    current.Kill();
                }
                current.WaitForExit(2000);
            }
            catch (Exception exception)
            {
                AppLog.Write("Could not stop whisper-server: " + exception.Message);
            }
            finally
            {
                current.Dispose();
            }
        }
    }

    internal sealed class MainForm : Form
    {
        private const int HotkeyId = 0x5357;
        private const int WmHotkey = 0x0312;
        private const string RecordingAlias = "opensuperwhisper_recording";

        private readonly string appDirectory;
        private readonly string whisperPath;
        private readonly string modelPath;
        private readonly string recordingsDirectory;
        private readonly Label statusLabel;
        private readonly Label detailLabel;
        private readonly Button recordButton;
        private readonly TextBox transcriptionBox;
        private readonly CheckBox autoPasteCheckBox;
        private readonly NotifyIcon trayIcon;
        private readonly HotkeyDefinition hotkey;
        private readonly LanguageDefinition language;
        private readonly int threadCount;
        private readonly WhisperServer whisperServer;

        private AppState state = AppState.Idle;
        private string currentRecordingPath;
        private IntPtr targetWindow;
        private bool allowExit;
        private bool hotkeyRegistered;
        private readonly bool startInBackground;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, UIntPtr extraInfo);

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern int mciSendString(string command, StringBuilder returnValue, int returnLength, IntPtr callback);

        [DllImport("winmm.dll", CharSet = CharSet.Auto)]
        private static extern bool mciGetErrorString(int errorCode, StringBuilder errorText, int errorTextSize);

        public MainForm(bool startInBackground)
        {
            this.startInBackground = startInBackground;
            appDirectory = AppDomain.CurrentDomain.BaseDirectory;
            hotkey = HotkeyDefinition.Load();
            language = LanguageDefinition.Load();
            threadCount = Math.Max(1, Math.Min(Environment.ProcessorCount, 8));
            whisperPath = Path.Combine(appDirectory, "whisper", "whisper-cli.exe");
            modelPath = Path.Combine(appDirectory, "models", "ggml-base.bin");
            string whisperServerPath = Path.Combine(appDirectory, "whisper", "whisper-server.exe");
            whisperServer = File.Exists(whisperServerPath)
                ? new WhisperServer(whisperServerPath, modelPath, language, threadCount)
                : null;
            recordingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "OpenSuperWhisper",
                "Recordings");

            Directory.CreateDirectory(recordingsDirectory);

            Text = "OpenSuperWhisper for Windows";
            ClientSize = new Size(620, 470);
            MinimumSize = new Size(560, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(248, 247, 244);
            Font = new Font("Segoe UI", 10F);

            Label titleLabel = new Label();
            titleLabel.Text = "OpenSuperWhisper";
            titleLabel.Font = new Font("Segoe UI Semibold", 22F, FontStyle.Bold);
            titleLabel.ForeColor = Color.FromArgb(28, 28, 28);
            titleLabel.AutoSize = true;
            titleLabel.Location = new Point(28, 22);
            Controls.Add(titleLabel);

            Label platformLabel = new Label();
            platformLabel.Text = "Native Windows dictation";
            platformLabel.ForeColor = Color.FromArgb(100, 100, 100);
            platformLabel.AutoSize = true;
            platformLabel.Location = new Point(32, 66);
            Controls.Add(platformLabel);

            Panel statusPanel = new Panel();
            statusPanel.BackColor = Color.White;
            statusPanel.BorderStyle = BorderStyle.FixedSingle;
            statusPanel.Location = new Point(30, 101);
            statusPanel.Size = new Size(560, 112);
            statusPanel.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(statusPanel);

            statusLabel = new Label();
            statusLabel.Text = "Ready";
            statusLabel.Font = new Font("Segoe UI Semibold", 16F, FontStyle.Bold);
            statusLabel.AutoSize = true;
            statusLabel.Location = new Point(20, 16);
            statusPanel.Controls.Add(statusLabel);

            detailLabel = new Label();
            detailLabel.Text = "Press " + hotkey.DisplayName + " to start recording. Press it again to stop.";
            detailLabel.ForeColor = Color.FromArgb(85, 85, 85);
            detailLabel.AutoSize = true;
            detailLabel.Location = new Point(23, 52);
            statusPanel.Controls.Add(detailLabel);

            recordButton = new Button();
            recordButton.Text = "Start recording  (" + hotkey.DisplayName + ")";
            recordButton.FlatStyle = FlatStyle.Flat;
            recordButton.FlatAppearance.BorderSize = 0;
            recordButton.BackColor = Color.FromArgb(33, 33, 33);
            recordButton.ForeColor = Color.White;
            recordButton.Size = new Size(210, 38);
            recordButton.Location = new Point(350, 64);
            recordButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            recordButton.Click += delegate { ToggleRecording(); };
            statusPanel.Controls.Add(recordButton);

            autoPasteCheckBox = new CheckBox();
            autoPasteCheckBox.Text = "Paste transcription into the active app automatically";
            autoPasteCheckBox.Checked = true;
            autoPasteCheckBox.AutoSize = true;
            autoPasteCheckBox.Location = new Point(32, 231);
            Controls.Add(autoPasteCheckBox);

            Label transcriptionLabel = new Label();
            transcriptionLabel.Text = "Latest transcription";
            transcriptionLabel.Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold);
            transcriptionLabel.AutoSize = true;
            transcriptionLabel.Location = new Point(30, 271);
            Controls.Add(transcriptionLabel);

            transcriptionBox = new TextBox();
            transcriptionBox.Multiline = true;
            transcriptionBox.ReadOnly = true;
            transcriptionBox.ScrollBars = ScrollBars.Vertical;
            transcriptionBox.BackColor = Color.White;
            transcriptionBox.BorderStyle = BorderStyle.FixedSingle;
            transcriptionBox.Location = new Point(30, 298);
            transcriptionBox.Size = new Size(560, 120);
            transcriptionBox.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            Controls.Add(transcriptionBox);

            Button recordingsButton = new Button();
            recordingsButton.Text = "Open recordings folder";
            recordingsButton.FlatStyle = FlatStyle.Flat;
            recordingsButton.Location = new Point(30, 430);
            recordingsButton.Size = new Size(165, 30);
            recordingsButton.Anchor = AnchorStyles.Bottom | AnchorStyles.Left;
            recordingsButton.Click += delegate
            {
                Process.Start("explorer.exe", "\"" + recordingsDirectory + "\"");
            };
            Controls.Add(recordingsButton);

            ContextMenuStrip trayMenu = new ContextMenuStrip();
            trayMenu.Items.Add("Show", null, delegate { ShowWindow(); });
            trayMenu.Items.Add("Change shortcut in CMD", null, delegate { OpenSettingsCommand(); });
            trayMenu.Items.Add("Exit", null, delegate
            {
                allowExit = true;
                Close();
            });

            trayIcon = new NotifyIcon();
            trayIcon.Icon = SystemIcons.Application;
            trayIcon.Text = "OpenSuperWhisper - " + hotkey.DisplayName;
            trayIcon.Visible = true;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.DoubleClick += delegate { ShowWindow(); };

            Resize += delegate
            {
                if (WindowState == FormWindowState.Minimized)
                {
                    Hide();
                }
            };

            FormClosing += OnFormClosing;
            Shown += OnShown;
        }

        private void OnShown(object sender, EventArgs eventArgs)
        {
            hotkeyRegistered = RegisterHotKey(
                Handle,
                HotkeyId,
                hotkey.Modifiers | HotkeyDefinition.ModNoRepeat,
                hotkey.VirtualKey);
            if (!hotkeyRegistered)
            {
                int error = Marshal.GetLastWin32Error();
                AppLog.Write(hotkey.DisplayName + " registration failed. Win32 error: " + error);
                SetStatus(
                    "Shortcut unavailable",
                    hotkey.DisplayName + " is already reserved. Run OpenSuperWhisper.cmd to choose another shortcut.",
                    Color.FromArgb(160, 65, 45));
            }
            else
            {
                AppLog.Write(hotkey.DisplayName + " registered successfully.");
            }

            if (!File.Exists(whisperPath) || !File.Exists(modelPath))
            {
                AppLog.Write("Runtime files missing. Engine: " + File.Exists(whisperPath) + ", model: " + File.Exists(modelPath));
                SetStatus(
                    "Setup incomplete",
                    "The Whisper engine or model is missing. Run setup-windows.ps1.",
                    Color.FromArgb(160, 65, 45));
                recordButton.Enabled = false;
            }
            else if (whisperServer != null)
            {
                whisperServer.StartInBackground();
            }

            if (startInBackground)
            {
                BeginInvoke((MethodInvoker)delegate { Hide(); });
            }
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WmHotkey && message.WParam.ToInt32() == HotkeyId)
            {
                AppLog.Write(hotkey.DisplayName + " received. Current state: " + state);
                ToggleRecording();
                return;
            }

            base.WndProc(ref message);
        }

        private void ToggleRecording()
        {
            if (state == AppState.Transcribing)
            {
                trayIcon.ShowBalloonTip(1200, "OpenSuperWhisper", "Please wait for transcription to finish.", ToolTipIcon.Info);
                return;
            }

            if (state == AppState.Recording)
            {
                StopAndTranscribe();
            }
            else
            {
                StartRecording();
            }
        }

        private void StartRecording()
        {
            try
            {
                AppLog.Write("Starting microphone recording.");
                targetWindow = GetForegroundWindow();
                currentRecordingPath = Path.Combine(
                    recordingsDirectory,
                    "recording-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".wav");

                SendMci("close " + RecordingAlias, true);
                SendMci("open new type waveaudio alias " + RecordingAlias, false);
                SendMci(
                    "set " + RecordingAlias +
                    " time format milliseconds format tag pcm bitspersample 16 channels 1 samplespersec 16000 bytespersec 32000 alignment 2",
                    false);
                SendMci("record " + RecordingAlias, false);

                state = AppState.Recording;
                SetStatus(
                    "Recording...",
                    "Speak now. Press " + hotkey.DisplayName + " again to stop and transcribe.",
                    Color.FromArgb(182, 45, 45));
                recordButton.Text = "Stop and transcribe  (" + hotkey.DisplayName + ")";
                recordButton.BackColor = Color.FromArgb(182, 45, 45);
                trayIcon.Text = "OpenSuperWhisper - Recording";
                AppLog.Write("Microphone recording started: " + currentRecordingPath);
            }
            catch (Exception exception)
            {
                AppLog.Write("Microphone start failed: " + exception);
                state = AppState.Idle;
                SendMci("close " + RecordingAlias, true);
                ShowError("Could not start the microphone", exception);
            }
        }

        private async void StopAndTranscribe()
        {
            try
            {
                AppLog.Write("Stopping microphone recording.");
                SendMci("stop " + RecordingAlias, false);
                SendMci("save " + RecordingAlias + " \"" + currentRecordingPath + "\"", false);
                SendMci("close " + RecordingAlias, true);

                state = AppState.Transcribing;
                SetStatus(
                    "Transcribing...",
                    "Local Whisper is converting your speech to text.",
                    Color.FromArgb(45, 91, 150));
                recordButton.Enabled = false;
                recordButton.Text = "Transcribing...";
                trayIcon.Text = "OpenSuperWhisper - Transcribing";

                string text = await Task.Run(delegate { return Transcribe(currentRecordingPath); });
                text = text.Trim();
                AppLog.Write("Transcription completed. Characters: " + text.Length);

                transcriptionBox.Text = text;
                if (text.Length == 0)
                {
                    SetStatus(
                        "No speech detected",
                        "Try again and speak closer to the microphone.",
                        Color.FromArgb(160, 100, 30));
                }
                else
                {
                    Clipboard.SetText(text);
                    SetStatus(
                        "Transcription ready",
                        autoPasteCheckBox.Checked
                            ? "The text was copied and pasted into the active app."
                            : "The text was copied to the clipboard.",
                        Color.FromArgb(33, 120, 74));

                    if (autoPasteCheckBox.Checked)
                    {
                        PasteIntoTargetWindow();
                    }
                }
            }
            catch (Exception exception)
            {
                AppLog.Write("Transcription failed: " + exception);
                ShowError("Transcription failed", exception);
            }
            finally
            {
                state = AppState.Idle;
                recordButton.Enabled = true;
                recordButton.Text = "Start recording  (" + hotkey.DisplayName + ")";
                recordButton.BackColor = Color.FromArgb(33, 33, 33);
                trayIcon.Text = "OpenSuperWhisper - " + hotkey.DisplayName;
            }
        }

        private string Transcribe(string audioPath)
        {
            if (whisperServer != null)
            {
                string serverText = whisperServer.Transcribe(audioPath);
                if (serverText != null)
                {
                    AppLog.Write("Transcribed with the resident whisper-server.");
                    return serverText;
                }

                AppLog.Write("whisper-server unavailable; transcribing with whisper-cli.");
            }

            return TranscribeWithCli(audioPath);
        }

        private string TranscribeWithCli(string audioPath)
        {
            string outputBase = Path.Combine(
                Path.GetDirectoryName(audioPath),
                Path.GetFileNameWithoutExtension(audioPath) + "-transcription");
            string outputTextPath = outputBase + ".txt";

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = whisperPath;
            startInfo.Arguments =
                "-m \"" + modelPath + "\" " +
                "-f \"" + audioPath + "\" " +
                "-l " + language.Code + " -t " + threadCount + " -nt -otxt -of \"" + outputBase + "\"";
            startInfo.WorkingDirectory = Path.GetDirectoryName(whisperPath);
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.RedirectStandardOutput = true;
            startInfo.RedirectStandardError = true;

            using (Process process = Process.Start(startInfo))
            {
                Task<string> standardOutputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> standardErrorTask = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                Task.WaitAll(standardOutputTask, standardErrorTask);

                string standardOutput = standardOutputTask.Result;
                string standardError = standardErrorTask.Result;

                if (process.ExitCode != 0)
                {
                    throw new InvalidOperationException(
                        "Whisper exited with code " + process.ExitCode + "." +
                        Environment.NewLine + standardError + Environment.NewLine + standardOutput);
                }
            }

            if (!File.Exists(outputTextPath))
            {
                throw new FileNotFoundException("Whisper did not create a transcription file.", outputTextPath);
            }

            return File.ReadAllText(outputTextPath, Encoding.UTF8);
        }

        private void PasteIntoTargetWindow()
        {
            if (targetWindow == IntPtr.Zero)
            {
                return;
            }

            SetForegroundWindow(targetWindow);
            System.Threading.Thread.Sleep(100);

            const byte VirtualKeyControl = 0x11;
            const byte VirtualKeyV = 0x56;
            const uint KeyUp = 0x0002;

            keybd_event(VirtualKeyControl, 0, 0, UIntPtr.Zero);
            keybd_event(VirtualKeyV, 0, 0, UIntPtr.Zero);
            keybd_event(VirtualKeyV, 0, KeyUp, UIntPtr.Zero);
            keybd_event(VirtualKeyControl, 0, KeyUp, UIntPtr.Zero);
        }

        private static void SendMci(string command, bool ignoreErrors)
        {
            int result = mciSendString(command, null, 0, IntPtr.Zero);
            if (result == 0 || ignoreErrors)
            {
                return;
            }

            StringBuilder errorText = new StringBuilder(256);
            mciGetErrorString(result, errorText, errorText.Capacity);
            throw new InvalidOperationException(
                "Windows audio error " + result + ": " + errorText + Environment.NewLine +
                "Command: " + command);
        }

        private void SetStatus(string status, string detail, Color color)
        {
            statusLabel.Text = status;
            statusLabel.ForeColor = color;
            detailLabel.Text = detail;
        }

        private void ShowError(string title, Exception exception)
        {
            state = AppState.Idle;
            SetStatus(title, exception.Message, Color.FromArgb(160, 65, 45));
            recordButton.Enabled = true;
            recordButton.Text = "Start recording  (" + hotkey.DisplayName + ")";
            recordButton.BackColor = Color.FromArgb(33, 33, 33);
            trayIcon.Text = "OpenSuperWhisper - " + hotkey.DisplayName;
            MessageBox.Show(this, exception.Message, title, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private void ShowWindow()
        {
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void OpenSettingsCommand()
        {
            string settingsPath = Path.Combine(appDirectory, "OpenSuperWhisper.cmd");
            if (!File.Exists(settingsPath))
            {
                MessageBox.Show(this, "OpenSuperWhisper.cmd was not found beside the app.", "Settings unavailable", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = settingsPath;
            startInfo.WorkingDirectory = appDirectory;
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }

        private void OnFormClosing(object sender, FormClosingEventArgs eventArgs)
        {
            if (!allowExit && eventArgs.CloseReason == CloseReason.UserClosing)
            {
                eventArgs.Cancel = true;
                Hide();
                trayIcon.ShowBalloonTip(
                    1200,
                    "OpenSuperWhisper is still running",
                    "Press " + hotkey.DisplayName + " to dictate, or use the tray icon to exit.",
                    ToolTipIcon.Info);
                return;
            }

            if (state == AppState.Recording)
            {
                SendMci("stop " + RecordingAlias, true);
                SendMci("close " + RecordingAlias, true);
            }

            if (whisperServer != null)
            {
                whisperServer.Shutdown();
            }

            if (hotkeyRegistered)
            {
                UnregisterHotKey(Handle, HotkeyId);
                AppLog.Write(hotkey.DisplayName + " unregistered.");
            }

            trayIcon.Visible = false;
            trayIcon.Dispose();
            AppLog.Write("Application exiting.");
        }

        private enum AppState
        {
            Idle,
            Recording,
            Transcribing
        }
    }
}
