// =====================================================================
//  Nook – schlanker Taskleisten-Ersatz für Windows 11
//  Eine einzige Quelldatei. Bauen mit build.bat (nutzt den C#-Compiler,
//  der in Windows bereits enthalten ist – keine Installation nötig).
//  Aussehen und Verhalten kommen ausschließlich aus config.json.
// =====================================================================
#if WINRT
using WinMC = global::Windows.Media.Control;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Nook
{
    // -----------------------------------------------------------------
    //  Einstieg
    // -----------------------------------------------------------------
    public static class Program
    {
        // Kanal: "NookTest.exe" läuft als Testversion neben der stabilen "Nook.exe"
        // (eigene Config im eigenen Ordner). Es läuft immer nur eine von beiden.
        public static bool IsTest;
        public static string Ch { get { return IsTest ? "_Test" : ""; } }
        static string OtherCh { get { return IsTest ? "" : "_Test"; } }
        public static string OtherExe;

        [STAThread]
        public static void Main(string[] args)
        {
            string exe = "";
            try { exe = System.IO.Path.GetFileNameWithoutExtension(Process.GetCurrentProcess().MainModule.FileName); } catch { }
            IsTest = exe.IndexOf("test", StringComparison.OrdinalIgnoreCase) >= 0;
            bool quit = args.Any(a => a.Equals("--quit", StringComparison.OrdinalIgnoreCase));
            bool created;
            var mutex = new Mutex(true, "Nook_SingleInstance_v1" + Ch, out created);
            if (!created)
            {
                try
                {
                    var ev = EventWaitHandle.OpenExisting((quit ? "Nook_Quit_v1" : "Nook_Toggle_v1") + Ch);
                    ev.Set();
                }
                catch { }
                return;
            }
            if (quit) return;
            StopOther();
            var app = new NookApp();
            app.Run();
            GC.KeepAlive(mutex);
        }

        // Die jeweils andere Version (stabil/Test) sauber beenden und ihren Pfad merken
        static void StopOther()
        {
            try
            {
                foreach (var p in Process.GetProcessesByName(IsTest ? "Nook" : "NookTest"))
                {
                    try { OtherExe = p.MainModule.FileName; } catch { }
                    p.Dispose();
                }
                var ev = EventWaitHandle.OpenExisting("Nook_Quit_v1" + OtherCh);
                ev.Set();
                for (int i = 0; i < 40; i++)
                {
                    Thread.Sleep(100);
                    try { using (Mutex.OpenExisting("Nook_SingleInstance_v1" + OtherCh)) { } }
                    catch { break; }   // andere Version ist beendet
                }
            }
            catch { }
        }
    }

    // -----------------------------------------------------------------
    //  JSON + Config
    // -----------------------------------------------------------------
    public static class Json
    {
        static readonly JavaScriptSerializer S = new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };

        public static object Parse(string s) { return S.DeserializeObject(s); }

        public static string Write(object o) { return Pretty(S.Serialize(o)); }

        static string Pretty(string j)
        {
            var sb = new StringBuilder();
            int ind = 0; bool q = false;
            for (int i = 0; i < j.Length; i++)
            {
                char c = j[i];
                if (q)
                {
                    sb.Append(c);
                    if (c == '\\') { i++; if (i < j.Length) sb.Append(j[i]); }
                    else if (c == '"') q = false;
                    continue;
                }
                switch (c)
                {
                    case '"': q = true; sb.Append(c); break;
                    case '{':
                    case '[':
                        if (i + 1 < j.Length && (j[i + 1] == '}' || j[i + 1] == ']')) { sb.Append(c).Append(j[i + 1]); i++; break; }
                        ind++; sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case '}':
                    case ']': ind--; sb.Append('\n').Append(' ', Math.Max(0, ind * 2)).Append(c); break;
                    case ',': sb.Append(c).Append('\n').Append(' ', ind * 2); break;
                    case ':': sb.Append(": "); break;
                    default: sb.Append(c); break;
                }
            }
            return sb.ToString();
        }

        public static IEnumerable<object> Seq(object v)
        {
            if (v == null || v is string) yield break;
            var e = v as System.Collections.IEnumerable;
            if (e == null || v is IDictionary<string, object>) yield break;
            foreach (var x in e) yield return x;
        }
    }

    public class Cfg
    {
        public const string Defaults = @"{
  ""version"": 1,
  ""language"": ""en"",
  ""position"": { ""edge"": ""top"", ""align"": ""center"", ""offset"": 8, ""sideMargin"": 16, ""monitor"": 0, ""dragMode"": ""reset"" },
  ""pill"": { ""visible"": true, ""mode"": ""always"", ""height"": 34, ""fontSize"": 13, ""roundness"": 100, ""border"": false,
              ""order"": [ ""mic"", ""clock"", ""date"", ""timer"", ""media"", ""windows"" ],
              ""showClock"": true, ""clock24h"": true, ""clockSeconds"": false, ""showDate"": false, ""dateFormat"": ""short"",
              ""showMedia"": true, ""mediaWidth"": 240, ""showMicMuted"": true,
              ""showWindows"": true, ""groupWindows"": true, ""maxWindowIcons"": 12, ""iconSize"": 0,
              ""clickTab"": ""start"", ""wheel"": ""volume"", ""middleClick"": ""close"",
              ""lipWidth"": 72, ""lipThickness"": 4, ""lipHideMs"": 650,
              ""reserveSpace"": false, ""hideInFullscreen"": true },
  ""panel"": { ""width"": 780, ""height"": 470, ""radius"": 24, ""borderWidth"": 0, ""shadow"": true, ""shadowStrength"": 0.45,
              ""animStyle"": ""morph"", ""animationMs"": 240, ""closeOnBlur"": true, ""startTab"": ""last"",
              ""tabStyle"": ""pill"", ""tabIcons"": ""text"", ""tabAlign"": ""center"",
              ""showClock"": true, ""showHelpButton"": true, ""showMenuButton"": true },
  ""theme"": { ""accent"": ""#D4B266"", ""background"": ""#0B0B0C"", ""gradient"": false, ""background2"": ""#1B1A24"",
              ""surface"": ""#161618"", ""surface2"": ""#232326"", ""text"": ""#F2F2F0"", ""muted"": ""#8E8E93"",
              ""pillBackground"": ""#0B0B0C"", ""border"": """", ""opacity"": 0.98,
              ""cardStyle"": ""filled"", ""cardRadius"": 18, ""font"": ""Segoe UI Variable Display"", ""scale"": ""auto"" },
  ""tabs"": [ ""controls"", ""today"", ""apps"", ""taskbar"", ""clipboard"", ""power"" ],
  ""behavior"": { ""winKey"": true, ""winTab"": ""same"", ""hotkey"": """", ""typeToSearch"": ""apps"", ""hideWindowsTaskbar"": true, ""autostart"": false },
  ""controls"": { ""tiles"": [ ""wifi"", ""bluetooth"", ""audio"", ""night"", ""dark"", ""mic"" ], ""showVolume"": true, ""showMedia"": true },
  ""today"": { ""showWeather"": true, ""showTimer"": true, ""showTodos"": true, ""weatherCity"": """", ""weatherUnit"": ""C"", ""timerPresets"": [ 1, 5, 10, 15, 20 ] },
  ""apps"": { ""showFavorites"": true, ""recentCount"": 5, ""compact"": false, ""showKind"": true,
             ""quickLinks"": [ { ""name"": ""Gmail"", ""target"": ""https://mail.google.com"" } ],
             ""webSearch"": ""https://www.google.com/search?q="", ""hideUninstallers"": true },
  ""taskbar"": { ""group"": true },
  ""clipboard"": { ""enabled"": true, ""maxItems"": 60, ""persist"": true, ""defaultFilter"": ""all"" },
  ""power"": { ""buttons"": [ ""lock"", ""sleep"", ""logoff"", ""restart"", ""shutdown"" ], ""confirm"": true }
}";

        public Dictionary<string, object> Root;
        public string Error;

        public static Cfg Load(string path)
        {
            var c = new Cfg();
            var def = (Dictionary<string, object>)Json.Parse(Defaults);
            Dictionary<string, object> user = null;
            try
            {
                if (File.Exists(path)) user = Json.Parse(File.ReadAllText(path, Encoding.UTF8)) as Dictionary<string, object>;
                else File.WriteAllText(path, Json.Write(def), new UTF8Encoding(false));
            }
            catch (Exception ex) { c.Error = ex.Message; }
            c.Root = user == null ? def : Merge(def, user);
            return c;
        }

        static Dictionary<string, object> Merge(Dictionary<string, object> a, Dictionary<string, object> b)
        {
            var r = new Dictionary<string, object>(a);
            foreach (var kv in b)
            {
                object av;
                var ad = r.TryGetValue(kv.Key, out av) ? av as Dictionary<string, object> : null;
                var bd = kv.Value as Dictionary<string, object>;
                r[kv.Key] = (ad != null && bd != null) ? Merge(ad, bd) : kv.Value;
            }
            return r;
        }

        public object Get(string path)
        {
            object cur = Root;
            foreach (var p in path.Split('.'))
            {
                var d = cur as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(p, out cur)) return null;
            }
            return cur;
        }

        public string S(string p, string d)
        {
            var v = Get(p);
            return v == null ? d : Convert.ToString(v, CultureInfo.InvariantCulture);
        }

        public double D(string p, double d)
        {
            var v = Get(p);
            if (v == null || v is string || v is bool) return d;
            try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return d; }
        }

        public bool B(string p, bool d)
        {
            var v = Get(p);
            return v is bool ? (bool)v : d;
        }

        public List<object> L(string p) { return Json.Seq(Get(p)).ToList(); }
    }

    // -----------------------------------------------------------------
    //  Persistenter Zustand (Favoriten, Verlauf, To-dos …)
    // -----------------------------------------------------------------
    public class Store
    {
        readonly string path;
        public Dictionary<string, object> D;

        public Store(string p)
        {
            path = p;
            try { if (File.Exists(p)) D = Json.Parse(File.ReadAllText(p, Encoding.UTF8)) as Dictionary<string, object>; } catch { }
            if (D == null) D = new Dictionary<string, object>();
        }

        public List<string> Strs(string k)
        {
            object v;
            if (!D.TryGetValue(k, out v)) return new List<string>();
            return Json.Seq(v).Select(x => Convert.ToString(x)).ToList();
        }

        public string Str(string k, string d)
        {
            object v;
            return D.TryGetValue(k, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : d;
        }

        public void Set(string k, object v) { D[k] = v; Save(); }
        public void Remove(string k) { if (D.Remove(k)) Save(); }

        public void Save()
        {
            try { File.WriteAllText(path, Json.Write(D), new UTF8Encoding(false)); } catch { }
        }
    }

    // -----------------------------------------------------------------
    //  Sprache: Englisch (Standard) oder Deutsch. Texte stehen im Code auf Deutsch,
    //  Tr() liefert bei Englisch die Übersetzung.
    // -----------------------------------------------------------------
    public static class Loc
    {
        public static bool En = true;

        static readonly Dictionary<string, string> Map = new Dictionary<string, string>
        {
            { "WLAN", "Wi-Fi" },
            { "Ausgabe", "Output" },
            { "Ausgabe: ", "Output: " },
            { "Gerade spielt keine App Ton ab.", "No app is playing sound right now." },
            { "Sound-Einstellungen", "Sound settings" },
            { "Lautstärkemixer", "Volume mixer" },
            { "Gerät konnte nicht gewechselt werden", "Couldn't switch the device" },
            { "Systemsounds", "System sounds" },
            { "Mikrofon", "Microphone" },
            { "Kein Mikrofon gefunden", "No microphone found" },
            { "Mikrofon stumm – klicken zum Einschalten", "Microphone muted – click to unmute" },
            { "Mikro stumm anzeigen", "Show muted microphone" },
            { "rotes Symbol in der Pille", "red icon in the pill" },
            { "Alt+1 … 6", "Alt+1 … 6" },
            { "Audio-Fenster: ↑ ↓ ← →", "Audio window: ↑ ↓ ← →" },
            { "Gerät wählen, App-Lautstärke", "Pick device, app volume" },
            { "Wie Start-Tab", "Same as start tab" },
            { "Start-Tab", "Start tab" },
            { "Tab beim Öffnen", "Tab shown when opening" },
            { "Uhr in der Tab-Leiste", "Clock in the tab bar" },
            { " Min", " min" },
            { " Std", " h" },
            { " T", " d" },
            { "jetzt", "now" },
            { "Kein Gerät", "No device" },
            { "Lautsprecher", "Speakers" },
            { "WinRT-Aufruf fehlgeschlagen", "WinRT call failed" },
            { "Ort nicht gefunden", "Location not found" },
            { "Klar", "Clear" },
            { "Überwiegend klar", "Mostly clear" },
            { "Teils bewölkt", "Partly cloudy" },
            { "Bedeckt", "Overcast" },
            { "Nebel", "Fog" },
            { "Nieselregen", "Drizzle" },
            { "Regen", "Rain" },
            { "Schnee", "Snow" },
            { "Regenschauer", "Showers" },
            { "Schneeschauer", "Snow showers" },
            { "Gewitter", "Thunderstorm" },
            { "Anwendung", "Application" },
            { "Config fehlerhaft – Standardwerte aktiv", "Config invalid – using defaults" },
            { "Tipp: Win öffnet Nook, F1 zeigt alle Tastenkürzel", "Tip: Win opens Nook, F1 shows all shortcuts" },
            { "Timer abgelaufen", "Timer finished" },
            { "Einstellungen", "Settings" },
            { "Strg+,", "Ctrl+," },
            { "Tastenkürzel", "Keyboard shortcuts" },
            { "Strg+Umschalt+Esc", "Ctrl+Shift+Esc" },
            { "Config-Ordner öffnen", "Open config folder" },
            { "Windows-Taskleiste zeigen", "Show Windows taskbar" },
            { "Windows-Taskleiste ausblenden", "Hide Windows taskbar" },
            { "Neu laden", "Reload" },
            { "Nook beenden", "Quit Nook" },
            { "Pille an Standardposition", "Reset pill position" },
            { "Einstellungen: Fehler – Details in data\\error.log", "Settings: error – details in data\\error.log" },
            { "Die Einstellungen konnten nicht gespeichert werden:\n", "The settings could not be saved:\n" },
            { "Lautstärke ", "Volume " },
            { "ddd d. MMM", "ddd, MMM d" },
            { "Tastenkürzel (F1)", "Keyboard shortcuts (F1)" },
            { "Steuerung", "Controls" },
            { "Nachtmodus", "Night light" },
            { "Dunkler Modus", "Dark mode" },
            { "Medien", "Media" },
            { "An", "On" },
            { "Aus", "Off" },
            { "Nicht verbunden", "Not connected" },
            { "Nichts wird abgespielt", "Nothing playing" },
            { "Starte Musik in Spotify oder im Browser", "Start music in Spotify or your browser" },
            { "Medien · einfache Variante (ohne Cover und Länge)", "Media · basic mode (no cover or length)" },
            { "Heute", "Today" },
            { "Diese Woche", "This week" },
            { "Start / Pause (Alt+S)", "Start / pause (Alt+S)" },
            { "Zurücksetzen (Alt+R)", "Reset (Alt+R)" },
            { "To-do hinzufügen …", "Add a to-do …" },
            { "Mausrad zum Einstellen", "Scroll to adjust" },
            { "Ort in der Config setzen", "Set a location in settings" },
            { "Lädt …", "Loading …" },
            { "ddd, d. MMM", "ddd, MMM d" },
            { "Weiter", "Resume" },
            { "Di", "Tu" },
            { "Mi", "We" },
            { "Do", "Th" },
            { "So", "Su" },
            { "d. MMM", "MMM d" },
            { ".–", "–" },
            { "KW ", "Week " },
            { "  ·  Tag ", "  ·  Day " },
            { " von ", " of " },
            { "Nichts zu tun. Schön.", "Nothing to do. Nice." },
            { "Apps durchsuchen oder rechnen …", "Search apps or calculate …" },
            { "Als Admin hier nicht möglich", "Can't run this one as admin" },
            { "Favoriten", "Favorites" },
            { "Zuletzt", "Recent" },
            { "Alle Apps", "All apps" },
            { "Apps werden geladen …", "Loading apps …" },
            { "Enter kopiert", "Enter copies" },
            { "Ergebnis kopiert", "Result copied" },
            { "„", "Search the web for “" },
            { "“ im Web suchen", "”" },
            { "Offene Fenster durchsuchen …", "Search open windows …" },
            { " offen", " open" },
            { "Keine Treffer.", "No results." },
            { "Keine Fenster geöffnet.", "No open windows." },
            { " Fenster", " windows" },
            { "  ·  minimiert", "  ·  minimized" },
            { "minimiert", "minimized" },
            { "Minimieren (Strg+M)", "Minimize (Ctrl+M)" },
            { "Schließen (Entf)", "Close (Del)" },
            { "Zwischenablage", "Clipboard" },
            { "Verlauf durchsuchen …", "Search history …" },
            { "Einfügen", "Paste" },
            { "Strg C", "Ctrl C" },
            { "Kopieren", "Copy" },
            { "Strg P", "Ctrl P" },
            { "Anheften", "Pin" },
            { "Entf", "Del" },
            { "Entfernen", "Remove" },
            { "Alle", "All" },
            { "Bilder", "Images" },
            { "Kopiert", "Copied" },
            { "Verlauf ist in der Config ausgeschaltet.", "History is turned off in settings." },
            { "Angeheftet", "Pinned" },
            { "Gestern", "Yesterday" },
            { "Früher", "Earlier" },
            { "Noch nichts kopiert.", "Nothing copied yet." },
            { "Bild · ", "Image · " },
            { "Kopierte Texte, Links und Bilder erscheinen hier.", "Copied text, links and images show up here." },
            { "Bild", "Image" },
            { " Zeichen · ", " characters · " },
            { " Wörter · ", " words · " },
            { " Zeilen", " lines" },
            { "Typ", "Type" },
            { "Größe", "Size" },
            { "Lösen", "Unpin" },
            { "Löschen", "Delete" },
            { "Sperren", "Lock" },
            { "Energie sparen", "Sleep" },
            { "Abmelden", "Sign out" },
            { "Neu starten", "Restart" },
            { "Herunterfahren", "Shut down" },
            { "Nochmal (Alt+", "Again (Alt+" },
            { " T ", " d " },
            { " Std ", " h " },
            { "Eingeschaltet seit ", "Up for " },
            { "Task beenden", "End task" },
            { "Aufgaben", "Tasks" },
            { "Öffnen", "Open" },
            { "Als Administrator ausführen", "Run as administrator" },
            { "Umschalt+Enter", "Shift+Enter" },
            { "Dateispeicherort öffnen", "Open file location" },
            { "Strg+O", "Ctrl+O" },
            { "Von Favoriten lösen", "Remove from favorites" },
            { "Zu Favoriten", "Add to favorites" },
            { "Strg+S", "Ctrl+S" },
            { "Deinstallieren", "Uninstall" },
            { " starten", "" },
            { "Fenster", "Windows" },
            { "Minimieren", "Minimize" },
            { "Alle Fenster schließen", "Close all windows" },
            { "Fenster schließen", "Close window" },
            { "Überall", "Everywhere" },
            { "Nook öffnen / schließen", "Open / close Nook" },
            { "Tippen", "Type" },
            { "Springt direkt zur Suche", "Jumps straight to search" },
            { "Schließen", "Close" },
            { "Diese Übersicht", "This overview" },
            { "Tab wechseln", "Switch tab" },
            { "Strg+1 … 6", "Ctrl+1 … 6" },
            { "Direkt zu Tab", "Go to tab" },
            { "Menütaste", "Menu key" },
            { "Menü zur Auswahl / Nook-Menü", "Context menu / Nook menu" },
            { "Suchen oder rechnen", "Search or calculate" },
            { "Auswählen, öffnen", "Select, open" },
            { "Als Administrator", "As administrator" },
            { "Speicherort öffnen", "Open file location" },
            { "Favorit an/aus", "Toggle favorite" },
            { "Fenster filtern", "Filter windows" },
            { "Fenster öffnen", "Open window" },
            { "Strg+M", "Ctrl+M" },
            { "Lautstärke", "Volume" },
            { "Leertaste", "Space" },
            { "Nächster / voriger Titel", "Next / previous track" },
            { "Umschalt+← →", "Shift+← →" },
            { "10 s spulen", "Seek 10 s" },
            { "Stumm", "Mute" },
            { "Kacheln", "Tiles" },
            { "Timer Start/Pause, Zurücksetzen", "Timer start/pause, reset" },
            { "Timer ±1 Minute", "Timer ±1 minute" },
            { "Neues To-do", "New to-do" },
            { "↑ ↓  Leertaste", "↑ ↓  Space" },
            { "To-do wählen, abhaken", "Select, check off to-do" },
            { "To-do löschen", "Delete to-do" },
            { "Strg+C  Strg+P", "Ctrl+C  Ctrl+P" },
            { "Kopieren, anheften", "Copy, pin" },
            { "Filter wechseln", "Switch filter" },
            { "Sperren, Energie sparen, Abmelden", "Lock, sleep, sign out" },
            { "Neu starten, Herunterfahren (2×)", "Restart, shut down (2×)" },
            { "F1 oder Esc schließt diese Übersicht  ·  Das ?-Symbol oben öffnet sie auch", "F1 or Esc closes this overview  ·  The ? icon at the top opens it too" },
            { "Ozean", "Ocean" },
            { "Lavendel", "Lavender" },
            { "Grafit", "Graphite" },
            { "Hell", "Light" },
            { "  ·  Einstellungen", "  ·  Settings" },
            { "Standard", "Defaults" },
            { "Config-Ordner", "Config folder" },
            { "Abbrechen", "Cancel" },
            { "Speichern", "Save" },
            { "Verwerfen?", "Discard?" },
            { "Wirklich?", "Sure?" },
            { "●  Nicht gespeichert  ·  Strg+S", "●  Unsaved  ·  Ctrl+S" },
            { "Farbe wählen (Enter)", "Pick colour (Enter)" },
            { "Zuletzt benutzter", "Last used" },
            { "Rand", "Edge" },
            { "Ausrichtung", "Alignment" },
            { "Abstand zum Rand", "Distance from edge" },
            { "0 = bündig, wie eine Notch", "0 = flush, like a notch" },
            { "Seitenabstand", "Side margin" },
            { "nur bei links/rechts", "only for left/right" },
            { "Pille verschieben", "Move the pill" },
            { "klicken, halten und ziehen", "click, hold and drag" },
            { "reset|Zurück beim Öffnen", "reset|Back when opening" },
            { "keep|Bleibt, wo sie ist", "keep|Stays where it is" },
            { "off|Nicht verschiebbar", "off|Fixed" },
            { "Bildschirm", "Monitor" },
            { "0 = Hauptbildschirm", "0 = main monitor" },
            { "Pille", "Pill" },
            { "Pille anzeigen", "Show pill" },
            { "Am Rand: nur ein Strich, Maus hin – Pille erscheint", "At edge: just a line – hover and the pill appears" },
            { "top:Oben", "top:Top" },
            { "bottom:Unten", "bottom:Bottom" },
            { "left:Links", "left:Left" },
            { "center:Mitte", "center:Center" },
            { "right:Rechts", "right:Right" },
            { "always:Immer", "always:Always" },
            { "lip:Am Rand", "lip:At edge" },
            { "hidden:Aus", "hidden:Off" },
            { "Höhe", "Height" },
            { "Schriftgröße", "Font size" },
            { "Uhrzeit", "Clock" },
            { "24-Stunden-Format", "24-hour format" },
            { "Datum", "Date" },
            { "Laufender Song", "Current song" },
            { "Offene Apps rechts", "Open apps on the right" },
            { "Klick öffnet/minimiert, Mittelklick schließt", "Click opens/minimizes, middle-click closes" },
            { "Platz reservieren", "Reserve space" },
            { "nur bei „Immer“: Fenster enden unter der Pille", "only with “Always”: windows end below the pill" },
            { "Bei Vollbild ausblenden", "Hide in fullscreen" },
            { "Spiele, Videos, F11", "Games, videos, F11" },
            { "Breite", "Width" },
            { "Eckenradius", "Corner radius" },
            { "0 = sofort", "0 = instant" },
            { "Schatten", "Shadow" },
            { "Schließen beim Wegklicken", "Close when clicking away" },
            { "Tab beim Klick auf die Pille", "Tab when clicking the pill" },
            { "Farben & Schrift", "Colours & font" },
            { "Akzent", "Accent" },
            { "Hintergrund", "Background" },
            { "Fläche", "Surface" },
            { "Fläche 2", "Surface 2" },
            { "Knöpfe, Felder", "Buttons, fields" },
            { "Text gedämpft", "Muted text" },
            { "Deckkraft", "Opacity" },
            { "Automatisch passt sich an 4K & Co. an", "Automatic adapts to 4K & co." },
            { "auto|Automatisch (", "auto|Automatic (" },
            { "Schrift", "Font" },
            { "Verhalten", "Behaviour" },
            { "Win-Taste öffnet Nook", "Win key opens Nook" },
            { "Win+E, Win+L usw. bleiben", "Win+E, Win+L etc. keep working" },
            { "Tab bei Win-Taste", "Tab for Win key" },
            { "Einfach lostippen", "Type to search" },
            { "Tippen ohne Suchfeld springt zur Suche", "Typing outside a search box jumps to search" },
            { "apps|Zur App-Suche", "apps|App search" },
            { "taskbar|Zur Fenster-Suche", "taskbar|Window search" },
            { "off|Aus", "off|Off" },
            { "Mit Windows starten", "Start with Windows" },
            { "Websuche", "Web search" },
            { "Eigene", "Custom" },
            { "Deinstallationsprogramme ausblenden", "Hide uninstallers" },
            { "Ort fürs Wetter", "Weather location" },
            { "leer = kein Wetter", "empty = no weather" },
            { "z. B. Hamburg", "e.g. London" },
            { "Einheit", "Unit" },
            { "Timer-Vorgaben", "Timer presets" },
            { "Minuten, mit Komma getrennt", "minutes, comma separated" },
            { "Zwischenablage & Power", "Clipboard & power" },
            { "Verlauf aktiv", "History on" },
            { "Einträge merken", "Items to keep" },
            { "Nach Neustart behalten", "Keep after restart" },
            { "nur Text, Bilder nie", "text only, never images" },
            { "Herunterfahren bestätigen", "Confirm shutdown" },
            { "zweimal drücken", "press twice" },
            { "Strg+S speichert  ·  Esc bricht ab  ·  Tab springt zum nächsten Feld", "Ctrl+S saves  ·  Esc cancels  ·  Tab moves to the next field" },
            { "Leertaste schaltet, Strg+↑/↓ verschiebt. Ausgeschaltete Tabs werden gar nicht geladen.", "Space toggles, Ctrl+↑/↓ moves. Disabled tabs aren't loaded at all." },
            { "Schnellzugriffe erscheinen als Chips über der App-Liste – Webadresse, Programm oder Ordner.", "Quick links show up as chips above the app list – web address, program or folder." },
            { "+ Schnellzugriff", "+ Quick link" },
            { "Pille ausgeblendet – Nook öffnet sich mit der Win-Taste", "Pill hidden – Nook opens with the Win key" },
            { "Erscheint, wenn die Maus den Strich am Rand berührt", "Appears when the mouse touches the line at the edge" },
            { "Zuhause 5G", "Home 5G" },
            { "Rechner", "Calculator" },
            { "Uhr, Woche, Timer, To-dos und Wetter", "Clock, week, timer, to-dos and weather" },
            { "Offene Fenster, nach App sortiert", "Open windows, grouped by app" },
            { "Verlauf mit Suche, Filtern und Vorschau", "History with search, filters and preview" },
            { "(in der Vorschau vereinfacht)", "(simplified in the preview)" },
            { "Sprache", "Language" },
            { " Treffer", " results" },
            { "0 = automatisch", "0 = automatic" },
            { "0 = kein Rahmen", "0 = no border" },
            { "1 Treffer", "1 result" },
            { "100 % = ganz rund, 0 % = eckig", "100 % = fully round, 0 % = square" },
            { "Alt+Leertaste", "Alt+Space" },
            { "Am Rand", "At edge" },
            { "Am Rand (Strich)", "At edge (line)" },
            { "Andere Version nicht gefunden", "Other version not found" },
            { "Animation & Schatten", "Animation & shadow" },
            { "Anzahl, 0 = aus", "Count, 0 = off" },
            { "Anzeige", "Display" },
            { "Art anzeigen", "Show type" },
            { "Ausblenden nach", "Hide after" },
            { "Auto = leicht aus der Textfarbe", "Auto = subtle, from the text colour" },
            { "Auto = wie Hintergrund", "Auto = same as background" },
            { "Beides", "Both" },
            { "Bleibt, wo sie ist", "Stays where it is" },
            { "Blende", "Fade" },
            { "Cover, Titel, Spulen", "Cover, title, seeking" },
            { "Datumsformat", "Date format" },
            { "Dauer", "Duration" },
            { "Desktop-Icons bleiben, wo sie sind", "Desktop icons stay where they are" },
            { "Einstellungsdatei", "Settings file" },
            { "F1 geht immer", "F1 always works" },
            { "Farbe und Breite unter Design bzw. Panel", "Colour and width under Design / Panel" },
            { "Farben", "Colours" },
            { "Favoriten anzeigen", "Show favorites" },
            { "Fenster einer App zusammenfassen", "Group windows of an app" },
            { "Filter beim Öffnen", "Filter when opening" },
            { "Flach", "Flat" },
            { "Form", "Shape" },
            { "Gefüllt", "Filled" },
            { "Gilt, wenn „Pille anzeigen“ auf „Am Rand“ steht.", "Applies when “Show pill” is set to “At edge”." },
            { "Gleiten", "Slide" },
            { "Größe & Form", "Size & shape" },
            { "Hilfe-Knopf", "Help button" },
            { "Hintergrund läuft schräg in eine zweite Farbe", "Background fades diagonally into a second colour" },
            { "Icon-Größe", "Icon size" },
            { "Im Panel: F1 zeigt alle Tastenkürzel.", "In the panel: F1 shows all shortcuts." },
            { "Immer", "Always" },
            { "Inhalt", "Content" },
            { "Kacheln an/aus und Reihenfolge – je drei pro Reihe.", "Tiles on/off and order – three per row." },
            { "Kacheln, Karten", "Tiles, cards" },
            { "Kacheln, Suchfelder, Bereiche", "Tiles, search fields, sections" },
            { "Karten", "Cards" },
            { "Kartenradius", "Card radius" },
            { "Kartenstil", "Card style" },
            { "Keins", "None" },
            { "Klick auf die Pille öffnet", "Clicking the pill opens" },
            { "Knöpfe an/aus und Reihenfolge.", "Buttons on/off and order." },
            { "Kompakte Liste", "Compact list" },
            { "Kurz", "Short" },
            { "Lang", "Long" },
            { "Lautstärke-Regler", "Volume slider" },
            { "Linie", "Underline" },
            { "Links", "Left" },
            { "Maus", "Mouse" },
            { "Mausrad über der Pille", "Scroll wheel over the pill" },
            { "Max. App-Icons", "Max. app icons" },
            { "Max. Breite des Songs", "Max. song width" },
            { "Medien-Karte", "Media card" },
            { "Menü-Knopf", "Menu button" },
            { "Mikro (nur wenn stumm)", "Microphone (only when muted)" },
            { "Mitte", "Center" },
            { "Mittelklick auf ein App-Icon", "Middle-click on an app icon" },
            { "Nach App gruppieren", "Group by app" },
            { "Nicht verschiebbar", "Not movable" },
            { "Nichts", "Nothing" },
            { "Nichts gefunden.", "Nothing found." },
            { "Nur Text", "Text only" },
            { "Oben", "Top" },
            { "Offene Apps", "Open apps" },
            { "Offene Apps anzeigen", "Show open apps" },
            { "Ordner öffnen", "Open folder" },
            { "Rahmen", "Border" },
            { "Rahmenbreite", "Border width" },
            { "Rahmenfarbe", "Border colour" },
            { "Rechts", "Right" },
            { "Rechtsklick geht immer", "Right-click always works" },
            { "Reihenfolge", "Order" },
            { "Reihenfolge in der Pille – Strg+↑/↓ oder die Pfeile verschieben.", "Order inside the pill – move with Ctrl+↑/↓ or the arrows." },
            { "Rundung", "Roundness" },
            { "Schattenstärke", "Shadow strength" },
            { "Schnellzugriffe", "Quick links" },
            { "Sekunden anzeigen", "Show seconds" },
            { "Sprache & Größe", "Language & size" },
            { "Stabile Version", "Stable version" },
            { "Strg+Alt+Leertaste", "Ctrl+Alt+Space" },
            { "Strg+Alt+N", "Ctrl+Alt+N" },
            { "Strg+Bild↑/↓  Bereich", "Ctrl+PgUp/PgDn  Section" },
            { "Strg+F  Suchen", "Ctrl+F  Search" },
            { "Strg+Leertaste", "Ctrl+Space" },
            { "Strg+S speichert  ·  Esc bricht ab  ·  Strg+F sucht  ·  Strg+Bild↑/↓ wechselt den Bereich  ·  Tab springt zum nächsten Feld  ·  Leertaste schaltet  ·  ← → ändert Werte", "Ctrl+S saves  ·  Esc cancels  ·  Ctrl+F searches  ·  Ctrl+PgUp/PgDn switches section  ·  Tab moves to the next field  ·  Space toggles  ·  ← → changes values" },
            { "Strg+Umschalt+Leertaste", "Ctrl+Shift+Space" },
            { "Strg+Umschalt+N", "Ctrl+Shift+N" },
            { "Strichbreite", "Line width" },
            { "Strichdicke", "Line thickness" },
            { "Suchen …", "Search …" },
            { "Symbole", "Icons" },
            { "Tab-Beschriftung", "Tab labels" },
            { "Tab-Leiste", "Tab bar" },
            { "Tab-Stil", "Tab style" },
            { "Tabs ausrichten", "Tab alignment" },
            { "Tastatur", "Keyboard" },
            { "Tastenkürzel ist schon belegt: ", "Shortcut is already taken: " },
            { "Testversion starten", "Start test version" },
            { "Testversion – läuft neben der stabilen Version, eigene Einstellungen", "Test version – runs instead of the stable one, with its own settings" },
            { "Timer (nur wenn er läuft)", "Timer (only while running)" },
            { "Timer-Karte", "Timer card" },
            { "Umriss", "Outline" },
            { "Unten", "Bottom" },
            { "Verlauf", "Gradient" },
            { "Verlaufsfarbe", "Gradient colour" },
            { "Verschieben", "Moving" },
            { "Vorlagen", "Presets" },
            { "Wetter-Karte", "Weather card" },
            { "Zahlen", "Numeric" },
            { "Zuletzt benutzt", "Recently used" },
            { "Zur App-Suche", "To app search" },
            { "Zur Fenster-Suche", "To window search" },
            { "Zur stabilen Version wechseln", "Switch to stable version" },
            { "Zurück beim Öffnen", "Returns when opening" },
            { "Zusätzliches Tastenkürzel", "Extra shortcut" },
            { "aus = einfache Liste aller Fenster", "off = plain list of all windows" },
            { "aus = jedes Fenster bekommt ein eigenes Icon", "off = every window gets its own icon" },
            { "config.json – zum Sichern oder Teilen", "config.json – for backup or sharing" },
            { "in der Testversion ohne Wirkung", "has no effect in the test version" },
            { "niedrigere Zeilen, kleinere Icons", "lower rows, smaller icons" },
            { "wenn die Maus die Pille verlässt", "after the mouse leaves the pill" },
            { "z. B. „Anwendung“ rechts in der Zeile", "e.g. “Application” at the right of the row" },
            { "Öffnen-Effekt", "Open effect" },
            { "öffnet und schließt Nook von überall", "opens and closes Nook from anywhere" },
            { "Allgemein", "General" },
            { "Vorschau", "Preview" },
            { "In der Testversion ohne Wirkung", "No effect in the test version" },
            { "Nook startet jetzt mit Windows", "Nook now starts with Windows" },
            { "Nook startet nicht mehr mit Windows", "Nook no longer starts with Windows" },
        };

        public static string Tr(string de)
        {
            if (!En || de == null) return de;
            string en;
            return Map.TryGetValue(de, out en) ? en : de;
        }
    }

    // -----------------------------------------------------------------
    //  Farben & Schrift
    // -----------------------------------------------------------------
    public static class Th
    {
        public static SolidColorBrush Bg, Surface, Surface2, Hover, Text, Muted, Accent, AccentSoft, AccentText, AccentHover, PillHover, Faint;
        public static Brush PillBg;
        public static Color AccentC, TextC;
        public static FontFamily Font, Icons, Sym;
        public static readonly CultureInfo De = new CultureInfo("de-DE");
        public static CultureInfo Cul = new CultureInfo("en-US");

        public static double K = 1;     // UI-Größe (1 = normal)
        public static double CardR = 18, CardBorderW, BorderW, ShadowOpacity = 0.45;
        public static Brush CardBg, CardBorder, PanelBg;
        public static Color BorderC;

        static Brush Grad(Color a, Color b)
        {
            var g = new LinearGradientBrush(a, b, new Point(0, 0), new Point(1, 1));
            g.Freeze();
            return g;
        }

        // Karte im gewählten Stil (gefüllt / Umriss / dezent)
        public static void StyleCard(Border b, double radiusAdd)
        {
            b.Background = CardBg;
            b.BorderBrush = CardBorder;
            b.BorderThickness = new Thickness(CardBorderW);
            b.CornerRadius = new CornerRadius(Math.Max(0, CardR + radiusAdd));
        }

        public static void Apply(Cfg c)
        {
            double op = Math.Max(0.3, Math.Min(1.0, c.D("theme.opacity", 0.98)));
            var bg = P(c.S("theme.background", "#0B0B0C"), Color.FromRgb(11, 11, 12));
            var pill = P(c.S("theme.pillBackground", ""), bg);
            var s1 = P(c.S("theme.surface", "#161618"), Color.FromRgb(22, 22, 24));
            var s2 = P(c.S("theme.surface2", "#232326"), Color.FromRgb(35, 35, 38));
            TextC = P(c.S("theme.text", "#F2F2F0"), Color.FromRgb(242, 242, 240));
            AccentC = P(c.S("theme.accent", "#D4B266"), Color.FromRgb(212, 178, 102));

            Bg = B(A(bg, (byte)(255 * op)));
            PillBg = B(A(pill, (byte)(255 * op)));
            PillHover = B(A(Mix(pill, TextC, 0.08), (byte)(255 * op)));
            Surface = B(s1);
            Surface2 = B(s2);
            Hover = B(Mix(s1, TextC, 0.07));
            Text = B(TextC);
            Muted = B(P(c.S("theme.muted", "#8E8E93"), Color.FromRgb(142, 142, 147)));
            Faint = B(A(TextC, 40));
            Accent = B(AccentC);
            AccentSoft = B(A(AccentC, 46));
            AccentHover = B(Mix(AccentC, Colors.White, 0.10));
            AccentText = B(Lum(AccentC) > 0.5 ? Color.FromRgb(24, 20, 12) : Colors.White);

            // Karten (Kacheln, Suchfelder, Bereiche)
            CardR = Math.Max(0, Math.Min(32, c.D("theme.cardRadius", 18)));
            string cs = c.S("theme.cardStyle", "filled");
            CardBg = cs == "filled" ? Surface : cs == "outline" ? B(Colors.Transparent) : B(A(s1, 110));
            CardBorderW = cs == "outline" ? 1.3 : 0;
            CardBorder = B(A(TextC, 46));
            // Rahmen um Panel und Pille
            BorderC = P(c.S("theme.border", ""), A(TextC, 40));
            BorderW = Math.Max(0, Math.Min(4, c.D("panel.borderWidth", 0)));
            // Verlauf
            if (c.B("theme.gradient", false))
            {
                var bg2 = P(c.S("theme.background2", "#1B1A24"), Mix(bg, AccentC, 0.18));
                PanelBg = Grad(A(bg, (byte)(255 * op)), A(bg2, (byte)(255 * op)));
                PillBg = Grad(A(pill, (byte)(255 * op)), A(Mix(pill, bg2, 0.6), (byte)(255 * op)));
            }
            else PanelBg = Bg;
            ShadowOpacity = Math.Max(0, Math.Min(1, c.D("panel.shadowStrength", 0.45)));

            Font = new FontFamily(c.S("theme.font", "Segoe UI Variable Display") + ", Segoe UI");
            Icons = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
            Sym = new FontFamily("Segoe UI Symbol, Segoe UI Emoji");
        }

        public static SolidColorBrush B(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        public static Color A(Color c, byte a) { return Color.FromArgb(a, c.R, c.G, c.B); }

        public static Color P(string s, Color def)
        {
            if (string.IsNullOrWhiteSpace(s)) return def;
            try { return (Color)ColorConverter.ConvertFromString(s.Trim()); } catch { return def; }
        }

        public static Color Mix(Color a, Color b, double t)
        {
            return Color.FromArgb(a.A, (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        public static double Lum(Color c) { return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0; }
    }

    // -----------------------------------------------------------------
    //  Kleine UI-Bausteine
    // -----------------------------------------------------------------
    public static class U
    {
        public static TextBlock T(string s, double size, Brush fg) { return T(s, size, fg, FontWeights.Normal); }

        public static TextBlock T(string s, double size, Brush fg, FontWeight w)
        {
            return new TextBlock
            {
                Text = Loc.Tr(s), FontSize = size, Foreground = fg, FontWeight = w, FontFamily = Th.Font,
                TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center
            };
        }

        public static TextBlock I(string glyph, double size, Brush fg)
        {
            return new TextBlock
            {
                Text = glyph, FontSize = size, Foreground = fg, FontFamily = Th.Icons,
                VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center
            };
        }

        public static Border Card(UIElement child)
        {
            var b = new Border { Padding = new Thickness(16), Child = child };
            Th.StyleCard(b, 0);
            return b;
        }

        public static GridLength Star(double v) { return new GridLength(v, GridUnitType.Star); }
        public static GridLength Px(double v) { return new GridLength(v); }
        public static GridLength Auto { get { return GridLength.Auto; } }

        public static Grid G(GridLength[] rows, GridLength[] cols)
        {
            var g = new Grid();
            if (rows != null) foreach (var r in rows) g.RowDefinitions.Add(new RowDefinition { Height = r });
            if (cols != null) foreach (var c in cols) g.ColumnDefinitions.Add(new ColumnDefinition { Width = c });
            return g;
        }

        public static T Put<T>(Grid g, T e, int row, int col) where T : UIElement
        {
            Grid.SetRow(e, row); Grid.SetColumn(e, col); g.Children.Add(e); return e;
        }

        public static Btn IconBtn(string glyph, double box, double size, Brush bg, Brush hover, Brush fg)
        {
            var b = new Btn(I(glyph, size, fg), bg, hover, box / 2, new Thickness(0));
            b.Width = box; b.Height = box;
            return b;
        }

        public static Btn Chip(string text, bool on)
        {
            var b = new Btn(T(text, 12.5, on ? Th.Accent : Th.Muted, FontWeights.SemiBold),
                on ? Th.AccentSoft : Th.Surface2, on ? Th.AccentSoft : Th.Hover, 13, new Thickness(12, 5, 12, 5));
            return b;
        }

        public static Border Section(string text)
        {
            var t = T(Loc.Tr(text).ToUpper(Th.Cul), 11, Th.Muted, FontWeights.Bold);
            return new Border { Child = t, Margin = new Thickness(12, 12, 0, 6) };
        }

        public static Border Letter(string name, double size)
        {
            string l = string.IsNullOrEmpty(name) ? "?" : name.Substring(0, 1).ToUpper(Th.Cul);
            var t = T(l, size * 0.45, Th.Text, FontWeights.Bold);
            t.HorizontalAlignment = HorizontalAlignment.Center;
            return new Border { Width = size, Height = size, CornerRadius = new CornerRadius(size * 0.28), Background = Th.Surface2, Child = t };
        }

        // Inhalt füllt den Platz; ist das Panel kleiner als die Mindestgröße, wird sauber verkleinert statt abgeschnitten
        public static FrameworkElement Fit(FrameworkElement content, double minW, double minH)
        {
            var vb = new Viewbox { Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, Child = content };
            var host = new Grid();
            host.Children.Add(vb);
            content.Width = minW; content.Height = minH;
            host.SizeChanged += (s, e) =>
            {
                double w = e.NewSize.Width, h = e.NewSize.Height;
                if (w <= 0 || h <= 0) return;
                double sc = Math.Min(1, Math.Min(w / minW, h / minH));
                content.Width = Math.Max(minW, w / sc);
                content.Height = Math.Max(minH, h / sc);
            };
            return host;
        }

        public static ScrollViewer Scroll(UIElement content)
        {
            return new ScrollViewer
            {
                Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false, PanningMode = PanningMode.VerticalOnly
            };
        }

        public static void Fade(UIElement e, double to, int ms)
        {
            e.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)));
        }

        public static string Ago(DateTime t)
        {
            var d = DateTime.Now - t;
            if (d.TotalSeconds < 60) return Loc.Tr("jetzt");
            if (d.TotalMinutes < 60) return ((int)d.TotalMinutes) + Loc.Tr(" Min");
            if (d.TotalHours < 24) return ((int)d.TotalHours) + Loc.Tr(" Std");
            return ((int)d.TotalDays) + Loc.Tr(" T");
        }
    }

    // Klickbare Fläche mit Hover – ersetzt Button (keine Templates nötig)
    public class Btn : Border
    {
        public event Action Click;
        Brush bg, hov;

        public Btn(UIElement content, Brush bg, Brush hover, double radius, Thickness pad)
        {
            this.bg = bg; hov = hover;
            Background = bg; Child = content; CornerRadius = new CornerRadius(radius); Padding = pad;
            Cursor = Cursors.Hand; Focusable = false;
            MouseEnter += (s, e) => { if (hov != null) Background = hov; };
            MouseLeave += (s, e) => { Background = this.bg; };
            MouseLeftButtonDown += (s, e) => { CaptureMouse(); e.Handled = true; };
            MouseLeftButtonUp += (s, e) =>
            {
                if (IsMouseCaptured)
                {
                    ReleaseMouseCapture();
                    var p = e.GetPosition(this);
                    if (p.X >= 0 && p.Y >= 0 && p.X <= ActualWidth && p.Y <= ActualHeight && Click != null) Click();
                }
                e.Handled = true;
            };
        }

        public void SetColors(Brush b, Brush h)
        {
            bg = b; hov = h;
            Background = (IsMouseOver && h != null) ? h : b;
        }
    }

    // Großer Regler wie im Vorbild (Füllbalken mit Prozent)
    public class FillSlider : Border
    {
        readonly Border fill;
        readonly TextBlock pct;
        public readonly TextBlock Icon;
        double v;
        public bool Dragging;
        public event Action<double> Changed;
        public event Action IconClick;

        public FillSlider(double h)
        {
            Height = h; CornerRadius = new CornerRadius(h / 2); Cursor = Cursors.Hand;
            Background = Th.CardBg; BorderBrush = Th.CardBorder; BorderThickness = new Thickness(Th.CardBorderW);
            fill = new Border { Background = Th.Accent, CornerRadius = new CornerRadius(h / 2), HorizontalAlignment = HorizontalAlignment.Left, Width = h };
            Icon = U.I("", 15, Th.AccentText);
            Icon.HorizontalAlignment = HorizontalAlignment.Left; Icon.Margin = new Thickness(h * 0.38, 0, 0, 0);
            pct = U.T("0 %", 13, Th.Text, FontWeights.SemiBold);
            pct.HorizontalAlignment = HorizontalAlignment.Right; pct.Margin = new Thickness(0, 0, 20, 0);
            var g = new Grid();
            g.Children.Add(fill); g.Children.Add(Icon); g.Children.Add(pct);
            Child = g;
            SizeChanged += (s, e) => Update();
            MouseLeftButtonDown += (s, e) =>
            {
                var x = e.GetPosition(this).X;
                if (x < h && IconClick != null) { IconClick(); e.Handled = true; return; }
                Dragging = true; CaptureMouse(); SetFromX(x); e.Handled = true;
            };
            MouseMove += (s, e) => { if (Dragging) SetFromX(e.GetPosition(this).X); };
            MouseLeftButtonUp += (s, e) => { Dragging = false; ReleaseMouseCapture(); };
            MouseWheel += (s, e) => { Value = v + (e.Delta > 0 ? 0.02 : -0.02); if (Changed != null) Changed(v); e.Handled = true; };
        }

        void SetFromX(double x)
        {
            double h = Height;
            Value = (x - h / 2) / Math.Max(1, ActualWidth - h);
            if (Changed != null) Changed(v);
        }

        public double Value
        {
            get { return v; }
            set { v = Math.Max(0, Math.Min(1, value)); Update(); }
        }

        public void SetIcon(string glyph) { Icon.Text = glyph; }

        void Update()
        {
            double h = Height;
            fill.Width = h + Math.Max(0, ActualWidth - h) * v;
            pct.Text = Math.Round(v * 100) + " %";
        }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Windows-API
    // -----------------------------------------------------------------
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

    [StructLayout(LayoutKind.Sequential)]
    public struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    [StructLayout(LayoutKind.Sequential)]
    public struct KBDLLHOOKSTRUCT { public uint vkCode; public uint scanCode; public uint flags; public uint time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAP { public int bmType, bmWidth, bmHeight, bmWidthBytes; public ushort bmPlanes, bmBitsPixel; public IntPtr bmBits; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize; public int biWidth, biHeight; public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage; public int biXPelsPerMeter, biYPelsPerMeter; public uint biClrUsed, biClrImportant;
        public uint pad0, pad1, pad2, pad3; // Platz für Farbmasken
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROPERTYKEY { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Explicit)]
    public struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(8)] public IntPtr p;
        [FieldOffset(8)] public short boolVal;
        [FieldOffset(16)] public long pad;
    }

    public static class N
    {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool BringWindowToTop(IntPtr h);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int cmd);
        [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsZoomed(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowTextLength(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(IntPtr h, StringBuilder sb, int max);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
        [DllImport("user32.dll")] public static extern bool AttachThreadInput(uint a, uint b, bool attach);
        [DllImport("kernel32.dll")] public static extern uint GetCurrentThreadId();
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();
        public delegate bool EnumProc(IntPtr h, IntPtr l);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr GetWindow(IntPtr h, uint cmd);
        [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr h, int idx);
        [DllImport("user32.dll")] public static extern int SetWindowLong(IntPtr h, int idx, int v);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, uint m, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr res);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "SendMessageTimeoutW")]
        public static extern IntPtr SendMessageTimeoutStr(IntPtr h, uint m, IntPtr w, string l, uint flags, uint timeout, out IntPtr res);
        [DllImport("user32.dll", EntryPoint = "GetClassLongPtrW")] public static extern IntPtr GetClassLongPtr(IntPtr h, int idx);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string cls, string title);
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
        public delegate IntPtr HookProc(int code, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern IntPtr SetWindowsHookEx(int id, HookProc proc, IntPtr mod, uint thread);
        [DllImport("user32.dll")] public static extern bool UnhookWindowsHookEx(IntPtr h);
        [DllImport("user32.dll")] public static extern IntPtr CallNextHookEx(IntPtr h, int code, IntPtr w, IntPtr l);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll")] public static extern bool LockWorkStation();
        [DllImport("user32.dll")] public static extern bool AddClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, uint mods, uint vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool RemoveClipboardFormatListener(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);
        [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr h, int attr, out int val, int size);
        [DllImport("shell32.dll")] public static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA d);
        [DllImport("shell32.dll")] public static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] public static extern bool SystemParametersInfo(uint action, uint param, ref RECT r, uint winIni);
        [DllImport("user32.dll")] public static extern IntPtr MonitorFromRect(ref RECT r, uint flags);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll")] public static extern bool SetProcessWorkingSetSize(IntPtr proc, IntPtr min, IntPtr max);
        public delegate void WinEventProc(IntPtr hook, uint ev, IntPtr hwnd, int obj, int child, uint thread, uint time);
        [DllImport("user32.dll")] public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr mod, WinEventProc proc, uint pid, uint tid, uint flags);
        [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr h);

        public static string ProcessPath(uint pid)
        {
            IntPtr h = OpenProcess(0x1000, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024); int n = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref n) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }
        [DllImport("powrprof.dll")] public static extern bool SetSuspendState(bool hibernate, bool force, bool disableWake);
        [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr h);
        [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr h, IntPtr dc);
        [DllImport("gdi32.dll")] public static extern int GetObject(IntPtr h, int size, out BITMAP bm);
        [DllImport("gdi32.dll")] public static extern int GetDIBits(IntPtr dc, IntPtr hbm, uint start, uint lines, [Out] byte[] bits, ref BITMAPINFOHEADER bi, uint usage);
        [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr h);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHCreateItemFromParsingName(string path, IntPtr pbc, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PROPVARIANT pv);

        public static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public static string ClassOf(IntPtr h)
        {
            var sb = new StringBuilder(256); GetClassName(h, sb, 256); return sb.ToString();
        }

        public static string TitleOf(IntPtr h)
        {
            int n = GetWindowTextLength(h);
            if (n <= 0) return "";
            var sb = new StringBuilder(n + 1); GetWindowText(h, sb, n + 1); return sb.ToString();
        }

        public static void AddExStyle(IntPtr h, int add)
        {
            SetWindowLong(h, -20, GetWindowLong(h, -20) | add);
        }

        // Fenster zuverlässig in den Vordergrund holen
        public static void ForceForeground(IntPtr h)
        {
            if (h == IntPtr.Zero) return;
            IntPtr fg = GetForegroundWindow();
            if (fg == h) return;
            uint pid;
            uint fgThread = GetWindowThreadProcessId(fg, out pid);
            uint me = GetCurrentThreadId();
            bool att = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
            BringWindowToTop(h);
            SetForegroundWindow(h);
            if (att) AttachThreadInput(me, fgThread, false);
            if (GetForegroundWindow() != h) SetForegroundWindow(h);
        }

        public static void Key(byte vk, bool ext)
        {
            uint f = ext ? 1u : 0u;
            keybd_event(vk, 0, f, UIntPtr.Zero);
            keybd_event(vk, 0, f | 2u, UIntPtr.Zero);
        }

        public static void SendCtrl(byte vk)
        {
            keybd_event(0x11, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 0, UIntPtr.Zero);
            keybd_event(vk, 0, 2, UIntPtr.Zero);
            keybd_event(0x11, 0, 2, UIntPtr.Zero);
        }

        // --- Windows-Taskleiste -------------------------------------
        static IEnumerable<IntPtr> TaskbarWindows()
        {
            var main = FindWindow("Shell_TrayWnd", null);
            if (main != IntPtr.Zero) yield return main;
            IntPtr h = IntPtr.Zero;
            while ((h = FindWindowEx(IntPtr.Zero, h, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero) yield return h;
        }

        public static bool AnyTaskbarVisible()
        {
            return TaskbarWindows().Any(IsWindowVisible);
        }

        public static void ShowTaskbars(bool show)
        {
            foreach (var h in TaskbarWindows()) ShowWindow(h, show ? 5 : 0);
        }

        public static int GetTaskbarState()
        {
            var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            d.hWnd = FindWindow("Shell_TrayWnd", null);
            return SHAppBarMessage(4, ref d).ToInt32();
        }

        public static void SetTaskbarState(int state)
        {
            var d = new APPBARDATA(); d.cbSize = Marshal.SizeOf(typeof(APPBARDATA));
            d.hWnd = FindWindow("Shell_TrayWnd", null);
            d.lParam = new IntPtr(state);
            SHAppBarMessage(10, ref d);
        }

        // --- Arbeitsbereich (ohne Broadcast -> Desktop-Icons bleiben stehen)
        public static RECT WorkAreaOf(RECT monitorBounds)
        {
            var r = monitorBounds;
            IntPtr m = MonitorFromRect(ref r, 2);
            var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            return GetMonitorInfo(m, ref mi) ? mi.rcWork : monitorBounds;
        }

        public static void SetWorkAreaSilent(RECT r)
        {
            SystemParametersInfo(0x2F, 0, ref r, 0);
        }

        public static bool Same(RECT a, RECT b)
        {
            return a.Left == b.Left && a.Top == b.Top && a.Right == b.Right && a.Bottom == b.Bottom;
        }

        public static string R2S(RECT r)
        {
            return r.Left + "," + r.Top + "," + r.Right + "," + r.Bottom;
        }

        public static bool S2R(string s, out RECT r)
        {
            r = new RECT();
            var p = (s ?? "").Split(',');
            int a, b, c, d;
            if (p.Length != 4 || !int.TryParse(p[0], out a) || !int.TryParse(p[1], out b) || !int.TryParse(p[2], out c) || !int.TryParse(p[3], out d)) return false;
            r.Left = a; r.Top = b; r.Right = c; r.Bottom = d;
            return true;
        }

        public static RECT ToRect(System.Drawing.Rectangle b)
        {
            return new RECT { Left = b.Left, Top = b.Top, Right = b.Right, Bottom = b.Bottom };
        }

        // --- Vollbild-Erkennung -------------------------------------
        public static bool FullscreenOnMonitorOf(IntPtr ownWindow)
        {
            int st;
            if (SHQueryUserNotificationState(out st) == 0 && (st == 2 || st == 3 || st == 4)) return true;

            IntPtr h = GetForegroundWindow();
            if (h == IntPtr.Zero || h == ownWindow) return false;
            uint pid; GetWindowThreadProcessId(h, out pid);
            if (pid == (uint)Process.GetCurrentProcess().Id) return false;
            string cls = ClassOf(h);
            if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Shell_SecondaryTrayWnd") return false;
            if (IsZoomed(h) || IsIconic(h)) return false;
            IntPtr mon = MonitorFromWindow(h, 2);
            if (ownWindow != IntPtr.Zero && mon != MonitorFromWindow(ownWindow, 2)) return false;
            var mi = new MONITORINFO(); mi.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (!GetMonitorInfo(mon, ref mi)) return false;
            RECT r;
            if (!GetWindowRect(h, out r)) return false;
            return r.Left <= mi.rcMonitor.Left && r.Top <= mi.rcMonitor.Top && r.Right >= mi.rcMonitor.Right && r.Bottom >= mi.rcMonitor.Bottom;
        }

        // --- Fenster-Icons ------------------------------------------
        public static ImageSource WindowIcon(IntPtr h)
        {
            try
            {
                IntPtr ic;
                SendMessageTimeout(h, 0x7F, new IntPtr(1), IntPtr.Zero, 2, 80, out ic);
                if (ic == IntPtr.Zero) SendMessageTimeout(h, 0x7F, new IntPtr(2), IntPtr.Zero, 2, 80, out ic);
                if (ic == IntPtr.Zero) ic = GetClassLongPtr(h, -14);
                if (ic == IntPtr.Zero) ic = GetClassLongPtr(h, -34);
                if (ic == IntPtr.Zero) return null;
                var bs = Imaging.CreateBitmapSourceFromHIcon(ic, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                bs.Freeze();
                return bs;
            }
            catch { return null; }
        }

        // Icon für beliebiges Shell-Element (auch Store-Apps)
        public static BitmapSource ShellIcon(string parsingName, int size)
        {
            object o = null;
            IntPtr hbm = IntPtr.Zero;
            try
            {
                Guid iid = new Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b");
                if (SHCreateItemFromParsingName(parsingName, IntPtr.Zero, ref iid, out o) != 0 || o == null) return null;
                var f = (IShellItemImageFactory)o;
                var sz = new SIZE { cx = size, cy = size };
                if (f.GetImage(sz, 0x4, out hbm) != 0 || hbm == IntPtr.Zero) return null;
                return FromHBitmap(hbm);
            }
            catch { return null; }
            finally
            {
                if (hbm != IntPtr.Zero) DeleteObject(hbm);
                if (o != null) Marshal.ReleaseComObject(o);
            }
        }

        static BitmapSource FromHBitmap(IntPtr hbm)
        {
            BITMAP bm;
            if (GetObject(hbm, Marshal.SizeOf(typeof(BITMAP)), out bm) == 0) return null;
            int w = bm.bmWidth, hgt = bm.bmHeight;
            var bi = new BITMAPINFOHEADER();
            bi.biSize = 40; bi.biWidth = w; bi.biHeight = -hgt; bi.biPlanes = 1; bi.biBitCount = 32; bi.biCompression = 0;
            var bits = new byte[w * hgt * 4];
            IntPtr dc = GetDC(IntPtr.Zero);
            try { if (GetDIBits(dc, hbm, 0, (uint)hgt, bits, ref bi, 0) == 0) return null; }
            finally { ReleaseDC(IntPtr.Zero, dc); }
            bool anyAlpha = false;
            for (int i = 3; i < bits.Length; i += 4) if (bits[i] != 0) { anyAlpha = true; break; }
            if (!anyAlpha) for (int i = 3; i < bits.Length; i += 4) bits[i] = 255;
            var src = BitmapSource.Create(w, hgt, 96, 96, PixelFormats.Pbgra32, null, bits, w * 4);
            src.Freeze();
            return src;
        }
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IShellItemImageFactory
    {
        [PreserveSig] int GetImage(SIZE size, int flags, out IntPtr phbm);
    }

    // -----------------------------------------------------------------
    //  Lautstärke (Core Audio)
    // -----------------------------------------------------------------
    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCom { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);
        [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int Item(uint index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int stgmAccess, out IPropertyStore props);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetState(out int state);
    }

    // Standard-Ausgabegerät setzen (von Windows selbst genutzt, nicht offiziell dokumentiert)
    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigCom { }

    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
        [PreserveSig] int SetEndpointVisibility([MarshalAs(UnmanagedType.LPWStr)] string id, int visible);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        [PreserveSig] int SetDuckingPreference(bool optOut);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public class AudioDevice { public string Id, Name; public bool IsDefault; }

    // Eine App im Lautstärkemixer (alle Sitzungen eines Programms zusammengefasst)
    public class AudioApp
    {
        public string Key, Name, Path;
        public uint Pid;
        public bool System;
        internal List<ISimpleAudioVolume> Vols = new List<ISimpleAudioVolume>();

        public double Volume
        {
            get { try { float f; return Vols.Count > 0 && Vols[0].GetMasterVolume(out f) == 0 ? f : 1; } catch { return 1; } }
            set { Guid g = Guid.Empty; foreach (var v in Vols) { try { v.SetMasterVolume((float)Math.Max(0, Math.Min(1, value)), ref g); } catch { } } }
        }

        public bool Muted
        {
            get { try { bool m; return Vols.Count > 0 && Vols[0].GetMute(out m) == 0 && m; } catch { return false; } }
            set { Guid g = Guid.Empty; foreach (var v in Vols) { try { v.SetMute(value, ref g); } catch { } } }
        }
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint count);
        [PreserveSig] int GetAt(uint i, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr p);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr p);
        [PreserveSig] int GetChannelCount(out uint c);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    public static class Audio
    {
        static IMMDeviceEnumerator en;

        static IMMDeviceEnumerator En()
        {
            if (en == null) en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCom();
            return en;
        }

        // flow: 0 = Wiedergabe, 1 = Aufnahme (Mikrofon)
        static IMMDevice Dev(int flow)
        {
            IMMDevice d;
            return En().GetDefaultAudioEndpoint(flow, 1, out d) == 0 ? d : null;
        }

        static IMMDevice Dev() { return Dev(0); }

        static IAudioEndpointVolume Ep(int flow)
        {
            var d = Dev(flow);
            if (d == null) return null;
            Guid iid = typeof(IAudioEndpointVolume).GUID;
            object o;
            if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return null;
            return o as IAudioEndpointVolume;
        }

        static IAudioEndpointVolume Ep() { return Ep(0); }

        static string NameOf(IMMDevice d, bool shortName)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0, out ps) != 0 || ps == null) return null;
            var k = new PROPERTYKEY { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };
            PROPVARIANT pv;
            if (ps.GetValue(ref k, out pv) != 0) return null;
            string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null;
            N.PropVariantClear(ref pv);
            if (string.IsNullOrEmpty(s)) return null;
            if (!shortName) return s;
            int i = s.IndexOf(" (", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : s;
        }

        // --- Ausgabegeräte ------------------------------------------
        public static List<AudioDevice> Outputs()
        {
            var list = new List<AudioDevice>();
            try
            {
                string def = null;
                var dd = Dev(0);
                if (dd != null) dd.GetId(out def);
                IMMDeviceCollection col;
                if (En().EnumAudioEndpoints(0, 1, out col) != 0 || col == null) return list;
                uint n; col.GetCount(out n);
                for (uint i = 0; i < n; i++)
                {
                    IMMDevice d;
                    if (col.Item(i, out d) != 0 || d == null) continue;
                    string id; d.GetId(out id);
                    list.Add(new AudioDevice { Id = id, Name = NameOf(d, false) ?? id, IsDefault = id == def });
                }
            }
            catch (Exception ex) { NookApp.Log(ex); }
            return list;
        }

        public static bool SetDefaultOutput(string id)
        {
            try
            {
                var pc = (IPolicyConfig)new PolicyConfigCom();
                bool ok = true;
                for (int role = 0; role < 3; role++) ok &= pc.SetDefaultEndpoint(id, role) == 0;
                Marshal.ReleaseComObject(pc);
                return ok;
            }
            catch (Exception ex) { NookApp.Log(ex); return false; }
        }

        // --- Lautstärke pro App -------------------------------------
        public static List<AudioApp> Apps()
        {
            var map = new Dictionary<string, AudioApp>();
            var order = new List<string>();
            try
            {
                var d = Dev(0);
                if (d == null) return new List<AudioApp>();
                Guid iid = typeof(IAudioSessionManager2).GUID;
                object o;
                if (d.Activate(ref iid, 23, IntPtr.Zero, out o) != 0) return new List<AudioApp>();
                var mgr = (IAudioSessionManager2)o;
                IAudioSessionEnumerator e;
                if (mgr.GetSessionEnumerator(out e) != 0 || e == null) return new List<AudioApp>();
                int n; e.GetCount(out n);
                uint me = (uint)Process.GetCurrentProcess().Id;
                for (int i = 0; i < n; i++)
                {
                    object so;
                    if (e.GetSession(i, out so) != 0 || so == null) continue;
                    var ctl = so as IAudioSessionControl2;
                    var vol = so as ISimpleAudioVolume;
                    if (ctl == null || vol == null) continue;
                    int state; ctl.GetState(out state);
                    if (state == 2) continue;                       // abgelaufen
                    uint pid; ctl.GetProcessId(out pid);
                    if (pid == me) continue;
                    bool sys = ctl.IsSystemSoundsSession() == 0;
                    string path = null, name = null;
                    if (sys) name = Loc.Tr("Systemsounds");
                    else
                    {
                        try { path = N.ProcessPath(pid); } catch { }
                        if (!string.IsNullOrEmpty(path))
                        {
                            name = System.IO.Path.GetFileNameWithoutExtension(path);
                            try { var desc = FileVersionInfo.GetVersionInfo(path).FileDescription; if (!string.IsNullOrWhiteSpace(desc)) name = desc.Trim(); } catch { }
                        }
                        string dn; if (ctl.GetDisplayName(out dn) == 0 && !string.IsNullOrWhiteSpace(dn) && !dn.StartsWith("@")) name = dn;
                        if (string.IsNullOrEmpty(name)) name = "PID " + pid;
                    }
                    string key = sys ? "#system" : (path ?? ("pid" + pid)).ToLowerInvariant();
                    AudioApp app;
                    if (!map.TryGetValue(key, out app))
                    {
                        app = new AudioApp { Key = key, Name = name, Path = path, Pid = pid, System = sys };
                        map[key] = app; order.Add(key);
                    }
                    app.Vols.Add(vol);
                }
            }
            catch (Exception ex) { NookApp.Log(ex); }
            return order.Select(k => map[k]).OrderBy(a => a.System ? 1 : 0).ToList();
        }

        // --- Mikrofon -----------------------------------------------
        public static bool HasMic() { try { return Dev(1) != null; } catch { return false; } }

        public static bool MicMuted()
        {
            try { var e = Ep(1); if (e == null) return false; bool m; e.GetMute(out m); return m; }
            catch { return false; }
        }

        public static void SetMicMuted(bool mute)
        {
            try { var e = Ep(1); if (e == null) return; Guid g = Guid.Empty; e.SetMute(mute, ref g); }
            catch { }
        }

        public static string MicName()
        {
            try { var d = Dev(1); return d == null ? null : NameOf(d, true); } catch { return null; }
        }

        public static double Get()
        {
            try { var e = Ep(); if (e == null) return -1; float f; e.GetMasterVolumeLevelScalar(out f); return f; }
            catch { return -1; }
        }

        public static void Set(double v)
        {
            try
            {
                var e = Ep(); if (e == null) return;
                Guid g = Guid.Empty;
                e.SetMasterVolumeLevelScalar((float)Math.Max(0, Math.Min(1, v)), ref g);
                bool m; e.GetMute(out m);
                if (m && v > 0) e.SetMute(false, ref g);
            }
            catch { }
        }

        public static bool Muted()
        {
            try { var e = Ep(); if (e == null) return false; bool m; e.GetMute(out m); return m; }
            catch { return false; }
        }

        public static void ToggleMute()
        {
            try { var e = Ep(); if (e == null) return; bool m; e.GetMute(out m); Guid g = Guid.Empty; e.SetMute(!m, ref g); }
            catch { }
        }

        public static string DeviceName()
        {
            try
            {
                var d = Dev(0);
                if (d == null) return Loc.Tr("Kein Gerät");
                return NameOf(d, true) ?? Loc.Tr("Lautsprecher");
            }
            catch { return Loc.Tr("Lautsprecher"); }
        }

        static int PropVariantClear(ref PROPVARIANT pv) { return N.PropVariantClear(ref pv); }

        public static string Glyph(double v, bool muted)
        {
            if (muted || v <= 0.001) return "";
            if (v < 0.34) return "";
            if (v < 0.67) return "";
            return "";
        }
    }

    // -----------------------------------------------------------------
    //  Win-Taste abfangen (nur wenn sie allein gedrückt wird)
    // -----------------------------------------------------------------
    public class KeyHook
    {
        N.HookProc proc;
        IntPtr hh;
        bool winDown, combo;
        readonly Action onWin;
        public bool Enabled = true;

        public KeyHook(Action onWin) { this.onWin = onWin; }

        public void Install()
        {
            if (hh != IntPtr.Zero) return;
            proc = Cb;
            using (var p = Process.GetCurrentProcess())
            using (var m = p.MainModule)
                hh = N.SetWindowsHookEx(13, proc, N.GetModuleHandle(m.ModuleName), 0);
        }

        public void Uninstall()
        {
            if (hh != IntPtr.Zero) { N.UnhookWindowsHookEx(hh); hh = IntPtr.Zero; }
        }

        // Win wird zunächst zurückgehalten. Kommt eine weitere Taste dazu, wird Win
        // nachgereicht (Win+E usw. funktionieren). Bleibt Win allein, sieht Windows
        // die Taste nie – so öffnet sich weder Startmenü noch Suche.
        uint winVk;

        IntPtr Cb(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0 && Enabled)
            {
                var k = (KBDLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(KBDLLHOOKSTRUCT));
                bool injected = (k.flags & 0x10) != 0;
                int msg = w.ToInt32();
                bool down = msg == 0x100 || msg == 0x104;
                bool up = msg == 0x101 || msg == 0x105;
                if (!injected)
                {
                    bool isWin = k.vkCode == 0x5B || k.vkCode == 0x5C;
                    if (isWin)
                    {
                        if (down)
                        {
                            if (!winDown) { winDown = true; combo = false; winVk = k.vkCode; }
                            if (!combo) return new IntPtr(1);
                        }
                        else if (up)
                        {
                            bool wasCombo = combo;
                            winDown = false; combo = false;
                            if (!wasCombo)
                            {
                                Application.Current.Dispatcher.BeginInvoke(onWin);
                                return new IntPtr(1);
                            }
                        }
                    }
                    else if (down && winDown && !combo)
                    {
                        combo = true;
                        N.keybd_event((byte)winVk, 0, 1, UIntPtr.Zero);
                        N.keybd_event((byte)k.vkCode, (byte)k.scanCode, (k.flags & 1) != 0 ? 1u : 0u, UIntPtr.Zero);
                        return new IntPtr(1);
                    }
                }
            }
            return N.CallNextHookEx(hh, code, w, l);
        }
    }

    // -----------------------------------------------------------------
    //  Medien (Spotify, Browser …)
    // -----------------------------------------------------------------
    public class MediaInfo
    {
        public string Title = "", Artist = "", App = "";
        public bool Playing;
        public ImageSource Cover;
        public double Pos = -1, Dur = -1;
        public bool CanSeek;
        public bool Has { get { return !string.IsNullOrEmpty(Title); } }
    }

    public abstract class MediaSvc
    {
        public volatile MediaInfo Info = new MediaInfo();
        public abstract void Poll();
        public abstract void Toggle();
        public abstract void Next();
        public abstract void Prev();
        public virtual bool Rich { get { return false; } }
        public virtual void Seek(double seconds) { }

        public static MediaSvc Create()
        {
#if WINRT
            try { return CreateRich(); } catch { }
#endif
            return new KeyMedia();
        }

#if WINRT
        [MethodImpl(MethodImplOptions.NoInlining)]
        static MediaSvc CreateRich() { return new RichMedia(); }
#endif
    }

    // Fallback: Medientasten + Spotify-Fenstertitel
    public class KeyMedia : MediaSvc
    {
        string lastTitle = "", lastArtist = "";

        public override void Poll()
        {
            var mi = new MediaInfo();
            bool running = false;
            foreach (var p in Process.GetProcessesByName("Spotify"))
            {
                try
                {
                    string t = p.MainWindowTitle;
                    if (string.IsNullOrEmpty(t)) continue;
                    running = true;
                    int i = t.IndexOf(" - ", StringComparison.Ordinal);
                    if (i > 0)
                    {
                        lastArtist = t.Substring(0, i); lastTitle = t.Substring(i + 3);
                        mi.Playing = true;
                    }
                }
                catch { }
                finally { p.Dispose(); }
            }
            if (running && lastTitle.Length > 0) { mi.Title = lastTitle; mi.Artist = lastArtist; mi.App = "Spotify"; }
            Info = mi;
        }

        public override void Toggle() { N.Key(0xB3, true); }
        public override void Next() { N.Key(0xB0, true); }
        public override void Prev() { N.Key(0xB1, true); }
    }

#if WINRT
    // Windows-Medienschnittstelle: Titel, Interpret, Cover, Fortschritt
    public class RichMedia : MediaSvc
    {
        WinMC.GlobalSystemMediaTransportControlsSessionManager mgr;
        WinMC.GlobalSystemMediaTransportControlsSession ses;
        string lastKey;
        ImageSource lastCover;
        readonly KeyMedia fb = new KeyMedia();
        bool useFb;
        int fails;

        public override bool Rich { get { return !useFb; } }

        public override void Poll()
        {
            if (useFb) { fb.Poll(); Info = fb.Info; return; }
            try { PollRich(); fails = 0; }
            catch (Exception ex)
            {
                if (fails == 0) NookApp.Log(ex);
                if (++fails >= 3) useFb = true;
            }
        }

        public override void Seek(double seconds)
        {
            var s = ses;
            if (s == null || useFb) return;
            long ticks = (long)(Math.Max(0, seconds) * 10000000.0);
            Fire(() => Await(s.TryChangePlaybackPositionAsync(ticks)));
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        void PollRich()
        {
            if (mgr == null) mgr = Await(WinMC.GlobalSystemMediaTransportControlsSessionManager.RequestAsync());
            var s = mgr.GetCurrentSession();
            ses = s;
            if (s == null) { Info = new MediaInfo(); return; }
            var props = Await(s.TryGetMediaPropertiesAsync());
            var mi = new MediaInfo();
            mi.Title = props.Title ?? "";
            mi.Artist = props.Artist ?? "";
            mi.App = Friendly(s.SourceAppUserModelId);
            var pb = s.GetPlaybackInfo();
            mi.Playing = pb != null && pb.PlaybackStatus == WinMC.GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            mi.CanSeek = pb != null && pb.Controls != null && pb.Controls.IsPlaybackPositionEnabled;
            var tl = s.GetTimelineProperties();
            if (tl != null)
            {
                double end = tl.EndTime.TotalSeconds, pos = tl.Position.TotalSeconds;
                if (mi.Playing)
                {
                    var age = DateTimeOffset.Now - tl.LastUpdatedTime;
                    if (age.TotalSeconds > 0 && age.TotalHours < 12) pos += age.TotalSeconds;
                }
                if (end > 0) { mi.Dur = end; mi.Pos = Math.Max(0, Math.Min(pos, end)); }
            }
            string key = mi.Title + "|" + mi.Artist;
            if (key != lastKey)
            {
                lastKey = key; lastCover = null;
                if (props.Thumbnail != null) { try { lastCover = LoadThumb(props.Thumbnail); } catch { } }
            }
            mi.Cover = lastCover;
            Info = mi;
        }

        // Wartet auf einen Windows-Aufruf, ohne die .NET-Hilfsbibliothek (AsTask) zu brauchen
        static T Await<T>(global::Windows.Foundation.IAsyncOperation<T> op)
        {
            var info = (global::Windows.Foundation.IAsyncInfo)op;
            var until = DateTime.UtcNow.AddSeconds(5);
            while (info.Status == global::Windows.Foundation.AsyncStatus.Started)
            {
                if (DateTime.UtcNow > until) { info.Cancel(); throw new TimeoutException(); }
                Thread.Sleep(4);
            }
            if (info.Status != global::Windows.Foundation.AsyncStatus.Completed)
                throw info.ErrorCode ?? new Exception(Loc.Tr("WinRT-Aufruf fehlgeschlagen"));
            return op.GetResults();
        }

        static ImageSource LoadThumb(global::Windows.Storage.Streams.IRandomAccessStreamReference r)
        {
            var st = Await(r.OpenReadAsync());
            using (st)
            {
                uint size = (uint)Math.Min(st.Size, 20UL * 1024 * 1024);
                if (size == 0) return null;
                var bytes = new byte[size];
                using (var rd = new global::Windows.Storage.Streams.DataReader(st.GetInputStreamAt(0)))
                {
                    uint got = Await(rd.LoadAsync(size));
                    if (got < size) Array.Resize(ref bytes, (int)got);
                    rd.ReadBytes(bytes);
                }
                var ms = new MemoryStream(bytes);
                var bi = new BitmapImage();
                bi.BeginInit(); bi.CacheOption = BitmapCacheOption.OnLoad; bi.DecodePixelWidth = 160; bi.StreamSource = ms; bi.EndInit();
                bi.Freeze();
                return bi;
            }
        }

        static string Friendly(string id)
        {
            if (string.IsNullOrEmpty(id)) return "";
            string s = id;
            if (s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) s = Path.GetFileNameWithoutExtension(s);
            int i = s.IndexOf('!'); if (i > 0) s = s.Substring(0, i);
            i = s.IndexOf('_'); if (i > 0) s = s.Substring(0, i);
            i = s.LastIndexOf('.'); if (i >= 0 && i < s.Length - 1) s = s.Substring(i + 1);
            string l = s.ToLowerInvariant();
            if (l.Contains("spotify")) return "Spotify";
            if (l == "msedge") return "Edge";
            if (l == "chrome") return "Chrome";
            if (l == "firefox") return "Firefox";
            if (l == "opera") return "Opera";
            return s.Length > 0 ? char.ToUpper(s[0]) + s.Substring(1) : s;
        }

        static void Fire(Action a) { ThreadPool.QueueUserWorkItem(_ => { try { a(); } catch { } }); }

        public override void Toggle()
        {
            var s = ses;
            if (s != null && !useFb) Fire(() => Await(s.TryTogglePlayPauseAsync())); else fb.Toggle();
        }

        public override void Next()
        {
            var s = ses;
            if (s != null && !useFb) Fire(() => Await(s.TrySkipNextAsync())); else fb.Next();
        }

        public override void Prev()
        {
            var s = ses;
            if (s != null && !useFb) Fire(() => Await(s.TrySkipPreviousAsync())); else fb.Prev();
        }
    }
#endif

    // -----------------------------------------------------------------
    //  Wetter (Open-Meteo, ohne Anmeldung)
    // -----------------------------------------------------------------
    public static class Weather
    {
        public static double Temp = double.NaN;
        public static int Code = -1;
        public static string Place = "";
        public static DateTime Fetched = DateTime.MinValue;
        static string coordFor; static double lat, lon;
        static int busy;

        public static void FetchAsync(string city, bool fahrenheit, Action done)
        {
            if (string.IsNullOrWhiteSpace(city)) return;
            if (Interlocked.Exchange(ref busy, 1) == 1) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                    using (var wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.UserAgent] = "Nook";
                        if (coordFor != city)
                        {
                            var g = Json.Parse(wc.DownloadString("https://geocoding-api.open-meteo.com/v1/search?count=1&language=de&name=" + Uri.EscapeDataString(city.Trim()))) as Dictionary<string, object>;
                            object res; Dictionary<string, object> first = null;
                            if (g != null && g.TryGetValue("results", out res)) first = Json.Seq(res).FirstOrDefault() as Dictionary<string, object>;
                            if (first == null) throw new Exception(Loc.Tr("Ort nicht gefunden"));
                            lat = Convert.ToDouble(first["latitude"], CultureInfo.InvariantCulture);
                            lon = Convert.ToDouble(first["longitude"], CultureInfo.InvariantCulture);
                            Place = Convert.ToString(first["name"]);
                            coordFor = city;
                        }
                        string url = string.Format(CultureInfo.InvariantCulture,
                            "https://api.open-meteo.com/v1/forecast?latitude={0}&longitude={1}&current=temperature_2m,weather_code{2}",
                            lat, lon, fahrenheit ? "&temperature_unit=fahrenheit" : "");
                        var d = Json.Parse(wc.DownloadString(url)) as Dictionary<string, object>;
                        var cur = (Dictionary<string, object>)d["current"];
                        Temp = Convert.ToDouble(cur["temperature_2m"], CultureInfo.InvariantCulture);
                        Code = Convert.ToInt32(cur["weather_code"], CultureInfo.InvariantCulture);
                        Fetched = DateTime.Now;
                    }
                }
                catch { Fetched = DateTime.Now.AddMinutes(-15); }
                finally
                {
                    busy = 0;
                    if (done != null && Application.Current != null) Application.Current.Dispatcher.BeginInvoke(done);
                }
            });
        }

        public static string Symbol(int c)
        {
            if (c == 0) return "☀";
            if (c <= 2) return "⛅";
            if (c == 3 || c == 45 || c == 48) return "☁";
            if ((c >= 71 && c <= 77) || c == 85 || c == 86) return "❄";
            if (c >= 95) return "⚡";
            if (c >= 51) return "☂";
            return "☁";
        }

        public static string Text(int c)
        {
            if (c == 0) return Loc.Tr("Klar");
            if (c == 1) return Loc.Tr("Überwiegend klar");
            if (c == 2) return Loc.Tr("Teils bewölkt");
            if (c == 3) return Loc.Tr("Bedeckt");
            if (c == 45 || c == 48) return Loc.Tr("Nebel");
            if (c >= 51 && c <= 57) return Loc.Tr("Nieselregen");
            if (c >= 61 && c <= 67) return Loc.Tr("Regen");
            if (c >= 71 && c <= 77) return Loc.Tr("Schnee");
            if (c >= 80 && c <= 82) return Loc.Tr("Regenschauer");
            if (c == 85 || c == 86) return Loc.Tr("Schneeschauer");
            if (c >= 95) return Loc.Tr("Gewitter");
            return "";
        }
    }

    // -----------------------------------------------------------------
    //  Rechner für die Suche
    // -----------------------------------------------------------------
    public static class Calc
    {
        public static bool TryEval(string input, out double r)
        {
            r = 0;
            if (string.IsNullOrWhiteSpace(input)) return false;
            string s = input.Trim();
            if (s.StartsWith("=")) s = s.Substring(1);
            s = Regex.Replace(s, @"(?<=\d)\s*[xX×]\s*(?=[\d(])", "*").Replace('÷', '/').Replace('×', '*');
            if (!Regex.IsMatch(s, @"\d")) return false;
            if (!Regex.IsMatch(s, @"[-+*/^%()]|sqrt|sin|cos|tan|log|ln|abs|pi")) return false;
            if (Regex.IsMatch(s, @"^\s*-?\d+([.,]\d+)?\s*$")) return false;
            try
            {
                var p = new Parser(s.ToLowerInvariant());
                r = p.Run();
                return !double.IsNaN(r) && !double.IsInfinity(r);
            }
            catch { return false; }
        }

        public static string Format(double v)
        {
            v = Math.Round(v, 10);
            return v.ToString("#,0.##########", Th.Cul);
        }

        public static string Plain(double v)
        {
            return Math.Round(v, 10).ToString("0.##########", Th.Cul);
        }

        class Parser
        {
            readonly string s; int i;
            public Parser(string s) { this.s = s; }

            public double Run()
            {
                double v = Expr();
                Ws();
                if (i < s.Length) throw new Exception();
                return v;
            }

            void Ws() { while (i < s.Length && char.IsWhiteSpace(s[i])) i++; }
            bool Eat(char c) { Ws(); if (i < s.Length && s[i] == c) { i++; return true; } return false; }

            double Expr()
            {
                double v = Term();
                while (true)
                {
                    if (Eat('+')) v += Term();
                    else if (Eat('-')) v -= Term();
                    else return v;
                }
            }

            double Term()
            {
                double v = Pow();
                while (true)
                {
                    if (Eat('*')) v *= Pow();
                    else if (Eat('/')) v /= Pow();
                    else { Ws(); if (i < s.Length && (s[i] == '(' || char.IsLetter(s[i]))) v *= Pow(); else return v; }
                }
            }

            double Pow()
            {
                double b = Unary();
                if (Eat('^')) return Math.Pow(b, Pow());
                return b;
            }

            double Unary()
            {
                if (Eat('-')) return -Unary();
                if (Eat('+')) return Unary();
                double v = Prim();
                while (Eat('%')) v /= 100.0;
                return v;
            }

            double Prim()
            {
                Ws();
                if (Eat('('))
                {
                    double v = Expr();
                    if (!Eat(')')) throw new Exception();
                    return v;
                }
                if (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == ','))
                {
                    int st = i;
                    while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == ',')) i++;
                    string num = s.Substring(st, i - st).Replace(',', '.');
                    return double.Parse(num, CultureInfo.InvariantCulture);
                }
                if (i < s.Length && char.IsLetter(s[i]))
                {
                    int st = i;
                    while (i < s.Length && char.IsLetter(s[i])) i++;
                    string id = s.Substring(st, i - st);
                    if (id == "pi") return Math.PI;
                    if (id == "e") return Math.E;
                    double a = Prim();
                    switch (id)
                    {
                        case "sqrt": return Math.Sqrt(a);
                        case "sin": return Math.Sin(a * Math.PI / 180);
                        case "cos": return Math.Cos(a * Math.PI / 180);
                        case "tan": return Math.Tan(a * Math.PI / 180);
                        case "log": return Math.Log10(a);
                        case "ln": return Math.Log(a);
                        case "abs": return Math.Abs(a);
                    }
                    throw new Exception();
                }
                throw new Exception();
            }
        }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Icons im Hintergrund laden
    // -----------------------------------------------------------------
    public static class IconWorker
    {
        static readonly BlockingCollection<Action> Q = new BlockingCollection<Action>();
        static Thread t;

        public static void Enqueue(Action a)
        {
            if (t == null)
            {
                t = new Thread(() => { foreach (var job in Q.GetConsumingEnumerable()) { try { job(); } catch { } } });
                t.SetApartmentState(ApartmentState.STA);
                t.IsBackground = true;
                t.Start();
            }
            Q.Add(a);
        }
    }

    // -----------------------------------------------------------------
    //  Installierte Apps (alles, was auch im Startmenü steht)
    // -----------------------------------------------------------------
    public class AppEntry
    {
        public string Name, Parse, Kind;
        public string Target;     // aufgelöste .exe (null = noch nicht gesucht, "" = keine)
        public bool Shell;
        public ImageSource Icon;
        public bool Requested;
        public Action OnIcon;

        public void RequestIcon()
        {
            if (Icon != null || Requested) return;
            Requested = true;
            var self = this;
            string pn = Shell ? "shell:AppsFolder\\" + Parse : Parse;
            var disp = Application.Current.Dispatcher;
            IconWorker.Enqueue(() =>
            {
                var bmp = N.ShellIcon(pn, 64);
                if (bmp == null) return;
                disp.BeginInvoke(new Action(() => { self.Icon = bmp; if (self.OnIcon != null) self.OnIcon(); }));
            });
        }
    }

    public class AppIndex
    {
        public List<AppEntry> All = new List<AppEntry>();
        public event Action Loaded;

        public void Load(bool hideUninstallers)
        {
            var disp = Application.Current.Dispatcher;
            var th = new Thread(() =>
            {
                List<AppEntry> l = null;
                try { l = FromShell(); } catch { }
                if (l == null || l.Count == 0) { try { l = FromStartMenu(); } catch { l = new List<AppEntry>(); } }
                if (hideUninstallers)
                    l = l.Where(a => { var n = a.Name.ToLowerInvariant(); return !n.Contains("uninstall") && !n.Contains("deinstall"); }).ToList();
                l = l.GroupBy(a => a.Name.ToLowerInvariant()).Select(g => g.First())
                     .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
                disp.BeginInvoke(new Action(() => { All = l; if (Loaded != null) Loaded(); }));
            });
            th.SetApartmentState(ApartmentState.STA);
            th.IsBackground = true;
            th.Start();
        }

        static object Call(object o, string name, BindingFlags f, params object[] args)
        {
            return o.GetType().InvokeMember(name, f, null, o, args);
        }

        static List<AppEntry> FromShell()
        {
            var list = new List<AppEntry>();
            var t = Type.GetTypeFromProgID("Shell.Application");
            object shell = Activator.CreateInstance(t);
            object folder = Call(shell, "NameSpace", BindingFlags.InvokeMethod, "shell:AppsFolder");
            object items = Call(folder, "Items", BindingFlags.InvokeMethod);
            int count = Convert.ToInt32(Call(items, "Count", BindingFlags.GetProperty));
            for (int i = 0; i < count; i++)
            {
                object it = null;
                try
                {
                    it = Call(items, "Item", BindingFlags.InvokeMethod, i);
                    string name = Convert.ToString(Call(it, "Name", BindingFlags.GetProperty));
                    string path = Convert.ToString(Call(it, "Path", BindingFlags.GetProperty));
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(path)) continue;
                    string kind = "Anwendung";
                    if (path.Contains("!")) kind = "App";
                    else if (path.StartsWith("http", StringComparison.OrdinalIgnoreCase)) kind = "Link";
                    else if (path.EndsWith(".url", StringComparison.OrdinalIgnoreCase)) kind = "Link";
                    list.Add(new AppEntry { Name = name, Parse = path, Kind = kind, Shell = true });
                }
                catch { }
                finally { if (it != null && Marshal.IsComObject(it)) Marshal.ReleaseComObject(it); }
            }
            Marshal.ReleaseComObject(items);
            Marshal.ReleaseComObject(folder);
            Marshal.ReleaseComObject(shell);
            return list;
        }

        static List<AppEntry> FromStartMenu()
        {
            var list = new List<AppEntry>();
            var dirs = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonStartMenu), "Programs"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs")
            };
            foreach (var d in dirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var f in Directory.EnumerateFiles(d, "*.lnk", SearchOption.AllDirectories))
                    list.Add(new AppEntry { Name = Path.GetFileNameWithoutExtension(f), Parse = f, Kind = "Anwendung" });
            }
            return list;
        }

        public AppEntry Find(string parse)
        {
            return All.FirstOrDefault(a => a.Parse == parse);
        }

        public static void Launch(AppEntry a)
        {
            try
            {
                if (a.Shell) Process.Start("explorer.exe", "\"shell:AppsFolder\\" + a.Parse + "\"");
                else Process.Start(a.Parse);
            }
            catch (Exception ex) { NookApp.Log(ex); }
        }

        public static void Open(string target)
        {
            try { Process.Start(target); } catch (Exception ex) { NookApp.Log(ex); }
        }

        public static int Score(string name, string q)
        {
            string n = name.ToLowerInvariant();
            if (n == q) return 1000;
            if (n.StartsWith(q)) return 800 - Math.Min(n.Length, 100);
            if (n.Contains(" " + q) || n.Contains("-" + q) || n.Contains("." + q)) return 600 - Math.Min(n.Length, 100);
            if (n.Contains(q)) return 400 - Math.Min(n.Length, 100);
            var initials = new string(n.Split(new[] { ' ', '-', '_', '.' }, StringSplitOptions.RemoveEmptyEntries).Select(w => w[0]).ToArray());
            if (q.Length >= 2 && initials.StartsWith(q)) return 350;
            int j = 0;
            foreach (char c in n) { if (j < q.Length && c == q[j]) j++; }
            if (j == q.Length && q.Length >= 2) return 100 - Math.Min(n.Length, 99);
            return -1;
        }
    }

    // -----------------------------------------------------------------
    //  Zwischenablage-Verlauf
    // -----------------------------------------------------------------
    public class ClipItem
    {
        public string Text;
        public BitmapSource Img;
        public DateTime Time;
        public bool Pinned;

        public string Kind
        {
            get
            {
                if (Img != null) return "image";
                Uri u;
                string t = Text == null ? "" : Text.Trim();
                if (t.Length < 2048 && !t.Contains("\n") && Uri.TryCreate(t, UriKind.Absolute, out u) && (u.Scheme == "http" || u.Scheme == "https")) return "link";
                return "text";
            }
        }
    }

    public class ClipHistory
    {
        public List<ClipItem> Items = new List<ClipItem>();
        public event Action Changed;
        public bool Enabled = true;
        int max = 60;
        bool persist = true;
        readonly string path;
        DateTime ignoreUntil;
        DispatcherTimer saveT;

        public ClipHistory(string path) { this.path = path; }

        public void ApplyCfg(Cfg c)
        {
            Enabled = c.B("clipboard.enabled", true);
            max = Math.Max(5, (int)c.D("clipboard.maxItems", 60));
            persist = c.B("clipboard.persist", true);
            if (!persist) { try { if (File.Exists(path)) File.Delete(path); } catch { } }
        }

        public void Load()
        {
            if (!persist || !File.Exists(path)) return;
            try
            {
                foreach (var o in Json.Seq(Json.Parse(File.ReadAllText(path, Encoding.UTF8))))
                {
                    var d = o as Dictionary<string, object>;
                    if (d == null || !d.ContainsKey("t")) continue;
                    long ticks = 0;
                    object tv; if (d.TryGetValue("time", out tv)) long.TryParse(Convert.ToString(tv), out ticks);
                    object pv; bool pinned = d.TryGetValue("pinned", out pv) && pv is bool && (bool)pv;
                    Items.Add(new ClipItem { Text = Convert.ToString(d["t"]), Time = ticks > 0 ? new DateTime(ticks) : DateTime.Now, Pinned = pinned });
                }
            }
            catch { }
        }

        public void OnUpdate() { ReadClipboard(0); }

        void ReadClipboard(int attempt)
        {
            if (!Enabled || DateTime.Now < ignoreUntil) return;
            try
            {
                if (Clipboard.ContainsData("ExcludeClipboardContentFromMonitorProcessing")) return;
                if (Clipboard.ContainsText())
                {
                    string t = Clipboard.GetText();
                    if (string.IsNullOrEmpty(t) || t.Trim().Length == 0 || t.Length > 200000) return;
                    AddText(t);
                }
                else if (Clipboard.ContainsImage())
                {
                    var img = Clipboard.GetImage();
                    if (img == null) return;
                    img.Freeze();
                    Items.Insert(0, new ClipItem { Img = img, Time = DateTime.Now });
                    Trim(); Fire();
                }
            }
            catch
            {
                if (attempt < 3)
                {
                    var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
                    t.Tick += (s, e) => { t.Stop(); ReadClipboard(attempt + 1); };
                    t.Start();
                }
            }
        }

        void AddText(string t)
        {
            var ex = Items.FirstOrDefault(x => x.Img == null && x.Text == t);
            if (ex != null) Items.Remove(ex); else ex = new ClipItem { Text = t };
            ex.Time = DateTime.Now;
            Items.Insert(0, ex);
            Trim(); Fire();
        }

        void Trim()
        {
            var unpinned = Items.Where(x => !x.Pinned).ToList();
            foreach (var x in unpinned.Skip(max)) Items.Remove(x);
            foreach (var x in Items.Where(x => x.Img != null).Skip(10).ToList()) Items.Remove(x);
        }

        void Fire()
        {
            if (Changed != null) Changed();
            SaveSoon();
        }

        public void Copy(ClipItem it)
        {
            ignoreUntil = DateTime.Now.AddMilliseconds(700);
            for (int i = 0; i < 6; i++)
            {
                try
                {
                    if (it.Img != null) Clipboard.SetImage(it.Img); else Clipboard.SetText(it.Text);
                    break;
                }
                catch { Thread.Sleep(40); }
            }
            Items.Remove(it); it.Time = DateTime.Now; Items.Insert(0, it);
            Fire();
        }

        public void Remove(ClipItem it) { Items.Remove(it); Fire(); }
        public void TogglePin(ClipItem it) { it.Pinned = !it.Pinned; Fire(); }

        void SaveSoon()
        {
            if (!persist) return;
            if (saveT == null)
            {
                saveT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                saveT.Tick += (s, e) => { saveT.Stop(); SaveNow(); };
            }
            saveT.Stop(); saveT.Start();
        }

        public void SaveNow()
        {
            if (!persist) return;
            try
            {
                var list = Items.Where(x => x.Img == null).Select(x => new Dictionary<string, object>
                {
                    { "t", x.Text }, { "time", x.Time.Ticks.ToString(CultureInfo.InvariantCulture) }, { "pinned", x.Pinned }
                }).ToList();
                File.WriteAllText(path, Json.Write(list), new UTF8Encoding(false));
            }
            catch { }
        }
    }

    // -----------------------------------------------------------------
    //  Countdown-Timer
    // -----------------------------------------------------------------
    public class Countdown
    {
        public int Total = 300;
        int pausedLeft = 300;
        DateTime end;
        public bool Running;

        public int Left
        {
            get { return Running ? Math.Max(0, (int)Math.Ceiling((end - DateTime.Now).TotalSeconds)) : pausedLeft; }
        }

        public void Set(int sec) { Total = Math.Max(1, sec); pausedLeft = Total; Running = false; }

        public void Start()
        {
            if (pausedLeft <= 0) pausedLeft = Total;
            end = DateTime.Now.AddSeconds(pausedLeft);
            Running = true;
        }

        public void Pause() { pausedLeft = Left; Running = false; }
        public void Reset() { Running = false; pausedLeft = Total; }

        public bool CheckFinished()
        {
            if (Running && Left <= 0) { Running = false; pausedLeft = 0; return true; }
            return false;
        }

        public static string Fmt(int sec)
        {
            int h = sec / 3600, m = (sec % 3600) / 60, s = sec % 60;
            return h > 0 ? string.Format("{0}:{1:00}:{2:00}", h, m, s) : string.Format("{0:00}:{1:00}", m, s);
        }
    }

    // -----------------------------------------------------------------
    //  Anwendung
    // -----------------------------------------------------------------
    public class NookApp : Application
    {
        public static event Action<IntPtr> ForegroundChanged;
        public const string Version = "2026.10.14-11";
        public static NookApp I;
        public Cfg Cfg;
        public Store Store;
        public PillWindow Pill;
        public PanelWindow Panel;
        public MediaSvc Media;
        public ClipHistory Clip;
        public AppIndex Apps;
        public readonly Countdown Timer = new Countdown();
        public IntPtr PrevForeground;
        public bool FullscreenActive, MicMuted;
        public string BaseDir, DataDir;

        KeyHook hook;
        N.WinEventProc fgProc;
        IntPtr fgHook;
        HwndSource msgWin;
        DispatcherTimer tick;
        System.Threading.Timer mediaTimer;
        int mediaBusy;
        bool tbHidden, cleaned;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            // Animationen mit höherer Bildrate (flüssiger auf 120/144-Hz-Monitoren)
            try { Timeline.DesiredFrameRateProperty.OverrideMetadata(typeof(Timeline), new FrameworkPropertyMetadata { DefaultValue = 144 }); } catch { }
            I = this;
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += (s, a) => { Log(a.Exception); a.Handled = true; };
            AppDomain.CurrentDomain.UnhandledException += (s, a) => { Log(a.ExceptionObject as Exception); try { RestoreTaskbar(); } catch { } };
            SessionEnding += (s, a) => Cleanup();

            BaseDir = AppDomain.CurrentDomain.BaseDirectory;
            DataDir = Path.Combine(BaseDir, "data");
            Directory.CreateDirectory(DataDir);
            AdoptOtherConfig();
            Store = new Store(Path.Combine(DataDir, "state.json"));
            if (Program.OtherExe != null) Store.Set("otherExe", Program.OtherExe);

            LoadConfig();
            LegacyRestore();
            Clip = new ClipHistory(Path.Combine(DataDir, "clipboard.json"));
            Clip.ApplyCfg(Cfg);
            Clip.Load();
            Apps = new AppIndex();
            Apps.Load(Cfg.B("apps.hideUninstallers", true));
            Media = MediaSvc.Create();

            var p = new HwndSourceParameters("NookMessages") { ParentWindow = new IntPtr(-3), WindowStyle = 0 };
            msgWin = new HwndSource(p);
            msgWin.AddHook(MsgProc);
            N.AddClipboardFormatListener(msgWin.Handle);

            hook = new KeyHook(() => TogglePanel("win"));
            hook.Install();
            fgProc = (hk, ev, hw, o, c, t, tm) =>
            {
                if (Pill != null) Pill.RefreshWindowsSoon();
                if (ForegroundChanged != null) ForegroundChanged(hw);
            };
            fgHook = N.SetWinEventHook(0x0003, 0x0003, IntPtr.Zero, fgProc, 0, 0, 0);

            BuildWindows();
            ApplySystem();
            ApplyHotkey();

            tick = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            tick.Tick += (s, a) => OnTick();
            tick.Start();
            mediaTimer = new System.Threading.Timer(MediaPoll, null, 400, 1000);
            var trimT = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
            trimT.Tick += (s2, e2) => { trimT.Stop(); Trim(); };
            trimT.Start();
            StartSignals();

            if (Cfg.Error != null) Pill.Flash(Loc.Tr("Config fehlerhaft – Standardwerte aktiv"), 5, false);
            else if (Store.Str("helpTipShown", null) == null)
            {
                Pill.Flash(Loc.Tr("Tipp: Win öffnet Nook, F1 zeigt alle Tastenkürzel"), 8, false);
                Store.Set("helpTipShown", "1");
            }
        }

        // Das Nook-Icon: Kachel, Pille, zwei Augen – in den Farben des Themes gezeichnet
        // Testversion: beim ersten Start Config und Daten der stabilen Version übernehmen
        void AdoptOtherConfig()
        {
            try
            {
                if (Program.OtherExe == null) return;
                string otherDir = Path.GetDirectoryName(Program.OtherExe);
                if (string.Equals(Path.GetFullPath(otherDir).TrimEnd('\\'), Path.GetFullPath(BaseDir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return;
                string myCfg = Path.Combine(BaseDir, "config.json");
                if (!File.Exists(myCfg) && File.Exists(Path.Combine(otherDir, "config.json")))
                    File.Copy(Path.Combine(otherDir, "config.json"), myCfg);
                foreach (var f in new[] { "state.json", "clipboard.json" })
                {
                    string src = Path.Combine(otherDir, "data", f), dst = Path.Combine(DataDir, f);
                    if (!File.Exists(dst) && File.Exists(src)) File.Copy(src, dst);
                }
            }
            catch (Exception ex) { Log(ex); }
        }

        // Zwischen stabiler Version und Testversion wechseln
        public void SwitchVersion()
        {
            string other = Store.Str("otherExe", null);
            if (other == null || !File.Exists(other)) { Pill.Flash(Loc.Tr("Andere Version nicht gefunden"), 3, false); return; }
            try { Process.Start(new ProcessStartInfo(other) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(other) }); }
            catch (Exception ex) { Log(ex); }
        }

        public static ImageSource Logo(int size) { return Logo(size, I != null ? I.Cfg : null); }

        public static ImageSource Logo(int size, Cfg c)
        {
            string bgHex = c != null ? c.S("theme.background", "#0E0E10") : "#0E0E10";
            var tile = Th.Mix(Th.P(bgHex, Color.FromRgb(14, 14, 16)), Th.TextC, 0.07);
            var pill = Th.AccentC;
            var eyes = Th.P(bgHex, Color.FromRgb(14, 14, 16));
            if (Math.Abs(Th.Lum(pill) - Th.Lum(eyes)) < 0.25) eyes = Th.Lum(pill) > 0.5 ? Color.FromRgb(14, 14, 16) : Colors.White;
            var g = new DrawingGroup();
            g.Children.Add(new GeometryDrawing(Th.B(tile), null, new RectangleGeometry(new Rect(40, 40, 944, 944), 230, 230)));
            g.Children.Add(new GeometryDrawing(Th.B(pill), null, new RectangleGeometry(new Rect(192, 372, 640, 280), 140, 140)));
            g.Children.Add(new GeometryDrawing(Th.B(eyes), null, new EllipseGeometry(new Point(412, 512), 40, 40)));
            g.Children.Add(new GeometryDrawing(Th.B(eyes), null, new EllipseGeometry(new Point(612, 512), 40, 40)));
            g.Children.Add(new GeometryDrawing(Brushes.Transparent, null, new RectangleGeometry(new Rect(0, 0, 1024, 1024))));
            g.Freeze();
            var img = new DrawingImage(g);
            img.Freeze();
            return img;
        }

        void LoadConfig()
        {
            Cfg = Cfg.Load(Path.Combine(BaseDir, "config.json"));
            Loc.En = Cfg.S("language", "en") != "de";
            Th.Cul = new CultureInfo(Loc.En ? "en-US" : "de-DE");
            Th.Apply(Cfg);
            Th.K = ComputeScale(Cfg);
        }

        // UI-Größe: "auto" richtet sich nach der Bildschirmhöhe (z. B. 4K), sonst fester Faktor
        public double ComputeScale(Cfg c)
        {
            double k;
            string v = c.S("theme.scale", "auto");
            if (v != "auto" && double.TryParse(v.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out k))
                return Math.Max(0.75, Math.Min(2.5, k));
            double h = MonitorDip().Height;
            if (h >= 2000) return 1.6;     // z. B. 4K bei 100 %
            if (h >= 1550) return 1.35;    // z. B. 4K bei 125 %
            if (h >= 1300) return 1.15;    // z. B. 1440p, 4K bei 150 %
            return 1.0;                    // Full HD und kleiner: normale Größe
        }

        void BuildWindows()
        {
            LoadPillPos();
            Pill = new PillWindow(this);
            Panel = new PanelWindow(this);
            Pill.UpdateVisibility();
            if (hook != null) hook.Enabled = Cfg.B("behavior.winKey", true);
        }

        void ApplySystem()
        {
            if (Cfg.B("behavior.hideWindowsTaskbar", true)) HideTaskbar(); else RestoreTaskbar();
            SetAutostart(Cfg.B("behavior.autostart", false));
        }

        IntPtr MsgProc(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x031D) Clip.OnUpdate();
            else if (msg == 0x0312 && w.ToInt32() == 0x4E4B) { TogglePanel("hotkey"); handled = true; }
            return IntPtr.Zero;
        }

        void StartSignals()
        {
            var toggle = new EventWaitHandle(false, EventResetMode.AutoReset, "Nook_Toggle_v1" + Program.Ch);
            var quit = new EventWaitHandle(false, EventResetMode.AutoReset, "Nook_Quit_v1" + Program.Ch);
            var th = new Thread(() =>
            {
                var hs = new WaitHandle[] { toggle, quit };
                while (true)
                {
                    int i = WaitHandle.WaitAny(hs);
                    if (i == 0) Dispatcher.BeginInvoke(new Action(() => TogglePanel("pill")));
                    else { Dispatcher.BeginInvoke(new Action(Quit)); return; }
                }
            });
            th.IsBackground = true;
            th.Start();
        }

        void OnTick()
        {
            bool fs = Cfg.B("pill.hideInFullscreen", true) && N.FullscreenOnMonitorOf(Pill.Handle);
            if (fs != FullscreenActive) { FullscreenActive = fs; Pill.UpdateVisibility(); }
            if (tbHidden && N.AnyTaskbarVisible()) N.ShowTaskbars(false);
            ApplyWorkArea();
            Pill.KeepTopmost();
            if (Cfg.B("pill.showMicMuted", true)) MicMuted = Audio.MicMuted();
            if (Timer.CheckFinished())
            {
                System.Media.SystemSounds.Exclamation.Play();
                Pill.Flash(Loc.Tr("Timer abgelaufen"), 0, true);
            }
            Pill.Refresh();
            if (Panel.IsOpen) Panel.Tick();
        }

        void MediaPoll(object _)
        {
            if (Interlocked.Exchange(ref mediaBusy, 1) == 1) return;
            try { Media.Poll(); }
            catch { }
            finally { mediaBusy = 0; }
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Pill != null) Pill.Refresh();
                if (Panel != null && Panel.IsOpen) Panel.MediaChanged();
            }));
        }

        // --- Panel ---------------------------------------------------
        public void TogglePanel(string source)
        {
            if (Panel == null) return;
            if (EditorWindow.Current != null) { EditorWindow.Current.Activate(); return; }
            if (Panel.IsOpen) { Panel.CloseAnimated(); return; }
            var fg = N.GetForegroundWindow();
            if (fg != Pill.Handle) PrevForeground = fg;
            if (PillPos != null && DragMode != "keep") SetPillPos(null);
            string tab;
            string winTab = Cfg.S("behavior.winTab", "same");
            string clickTab = Cfg.S("pill.clickTab", "start");
            if ((source == "win" || source == "hotkey") && winTab != "same") tab = winTab;
            else if (source == "pill" && clickTab != "start") tab = clickTab;
            else
            {
                tab = Cfg.S("panel.startTab", "last");
                if (tab == "last") tab = Store.Str("lastTab", "today");
            }
            Panel.Open(tab);
        }

        public void ClosePanel() { if (Panel != null && Panel.IsOpen) Panel.CloseAnimated(); }

        public void OnPanelOpened() { Pill.HideForPanel(true); }
        public void OnPanelClosed() { Pill.HideForPanel(false); }

        public void PasteToPrevious()
        {
            var target = PrevForeground;
            ClosePanel();
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            t.Tick += (s, e) =>
            {
                t.Stop();
                if (target != IntPtr.Zero && N.IsWindow(target)) N.ForceForeground(target);
                var t2 = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                t2.Tick += (s2, e2) => { t2.Stop(); N.SendCtrl(0x56); };
                t2.Start();
            };
            t.Start();
        }

        // --- Platzierung --------------------------------------------
        public System.Windows.Forms.Screen Screen()
        {
            var all = System.Windows.Forms.Screen.AllScreens;
            int i = (int)Cfg.D("position.monitor", 0);
            if (i <= 0 || i >= all.Length) return System.Windows.Forms.Screen.PrimaryScreen;
            return all[i];
        }

        public static double Scale()
        {
            using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) return g.DpiX / 96.0;
        }

        public Rect MonitorDip()
        {
            var b = Screen().Bounds;
            double s = Scale();
            return new Rect(b.Left / s, b.Top / s, b.Width / s, b.Height / s);
        }

        public bool Top { get { return Cfg.S("position.edge", "top") != "bottom"; } }
        public string Align { get { return Cfg.S("position.align", "center"); } }
        public double Offset { get { return Math.Max(0, Cfg.D("position.offset", 8)) * Th.K; } }
        public double Side { get { return Math.Max(0, Cfg.D("position.sideMargin", 16)) * Th.K; } }

        public HorizontalAlignment HAlign
        {
            get { return Align == "left" ? HorizontalAlignment.Left : Align == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center; }
        }

        // --- Position der Pille (Standard oder verschoben) ------------
        public Point? PillPos;   // Mitte X, Oberkante Y (DIP) – nur wenn verschoben

        public string DragMode { get { return Cfg.S("position.dragMode", "reset"); } }

        public Rect PillRect()
        {
            var sz = Pill != null ? Pill.VisualSize : new Size(120, 34);
            var mon = MonitorDip();
            double side = Side;
            double w = sz.Width, hh = sz.Height;
            double x, y;
            if (PillPos != null)
            {
                x = PillPos.Value.X - w / 2;
                y = PillPos.Value.Y;
            }
            else
            {
                x = Align == "left" ? mon.Left + side : Align == "right" ? mon.Right - side - w : mon.Left + (mon.Width - w) / 2;
                y = Top ? mon.Top + Offset : mon.Bottom - Offset - hh;
            }
            // Abstände aus der Config gelten immer als Mindestabstand zum Rand
            x = Math.Max(mon.Left + side, Math.Min(x, mon.Right - side - w));
            y = Math.Max(mon.Top + Offset, Math.Min(y, mon.Bottom - Offset - hh));
            return new Rect(x, y, w, hh);
        }

        public void SetPillPos(Point? p)
        {
            PillPos = p;
            if (DragMode == "keep" && p != null)
                Store.Set("pillPos", p.Value.X.ToString(CultureInfo.InvariantCulture) + ";" + p.Value.Y.ToString(CultureInfo.InvariantCulture));
            else Store.Remove("pillPos");
            if (Pill != null) { Pill.ApplyShape(); Pill.Reposition(); }
        }

        void LoadPillPos()
        {
            PillPos = null;
            if (DragMode != "keep") { Store.Remove("pillPos"); return; }
            var parts = (Store.Str("pillPos", "") ?? "").Split(';');
            double x, y;
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out x)
                && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out y))
                PillPos = new Point(x, y);
        }

        public CornerRadius Corners(double r)
        {
            if (Offset > 0.5) return new CornerRadius(r);
            return Top ? new CornerRadius(0, 0, r, r) : new CornerRadius(r, r, 0, 0);
        }

        // --- Taskleiste & Arbeitsbereich -------------------------------
        // Die Windows-Taskleiste wird nur versteckt (nicht auf "automatisch ausblenden"
        // gestellt). Den freien Platz gibt Nook über den Arbeitsbereich frei, und zwar
        // ohne Systemmeldung – so ordnet der Desktop seine Icons nicht neu an.

        // Altlast der ersten Version: Auto-Ausblenden wieder zurückstellen
        void LegacyRestore()
        {
            string old = Store.Str("taskbarOldState", null);
            int st;
            if (old != null && int.TryParse(old, out st)) N.SetTaskbarState(st);
            if (old != null) Store.Remove("taskbarOldState");
        }

        Dictionary<string, object> SavedWork()
        {
            object v;
            if (Store.D.TryGetValue("workAreas", out v) && v is Dictionary<string, object>) return (Dictionary<string, object>)v;
            return null;
        }

        void HideTaskbar()
        {
            if (tbHidden) return;
            if (SavedWork() == null)
            {
                var d = new Dictionary<string, object>();
                foreach (var sc in System.Windows.Forms.Screen.AllScreens)
                {
                    var b = N.ToRect(sc.Bounds);
                    d[N.R2S(b)] = N.R2S(N.WorkAreaOf(b));
                }
                Store.Set("workAreas", d);
            }
            N.ShowTaskbars(false);
            tbHidden = true;
            ApplyWorkArea();
        }

        public void RestoreTaskbar()
        {
            var saved = SavedWork();
            if (saved != null)
            {
                foreach (var kv in saved)
                {
                    RECT r;
                    if (N.S2R(Convert.ToString(kv.Value), out r)) N.SetWorkAreaSilent(r);
                }
                Store.Remove("workAreas");
            }
            if (tbHidden || saved != null) N.ShowTaskbars(true);
            tbHidden = false;
        }

        // Gewünschter Arbeitsbereich je Bildschirm; wird jede Sekunde geprüft,
        // weil Explorer ihn gelegentlich zurücksetzt.
        void ApplyWorkArea()
        {
            if (!tbHidden) return;
            var saved = SavedWork();
            var mine = Screen();
            bool reserve = Cfg.B("pill.reserveSpace", false) && Pill != null && Pill.Mode == "always";
            int strip = (int)Math.Ceiling((Offset + Math.Max(22, Cfg.D("pill.height", 34)) + 4) * Scale());
            foreach (var sc in System.Windows.Forms.Screen.AllScreens)
            {
                var b = N.ToRect(sc.Bounds);
                var want = b;
                if (reserve && sc.DeviceName == mine.DeviceName)
                {
                    if (Top) want.Top += strip; else want.Bottom -= strip;
                }
                if (!N.Same(N.WorkAreaOf(b), want)) N.SetWorkAreaSilent(want);
            }
        }

        public bool TaskbarHidden { get { return tbHidden; } }

        public void ToggleWindowsTaskbar()
        {
            if (tbHidden) RestoreTaskbar(); else HideTaskbar();
        }

        void SetAutostart(bool on)
        {
            if (Program.IsTest) return;     // Testversion fasst den Autostart nicht an
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    string exe = Process.GetCurrentProcess().MainModule.FileName;
                    if (on) k.SetValue("Nook", "\"" + exe + "\"");
                    else if (k.GetValue("Nook") != null) k.DeleteValue("Nook");
                }
            }
            catch { }
        }

        // Autostart direkt aus dem Rechtsklick-Menü umschalten (wird in config.json gespeichert)
        void ToggleAutostart()
        {
            bool on = !Cfg.B("behavior.autostart", false);
            object b;
            var beh = Cfg.Root.TryGetValue("behavior", out b) ? b as Dictionary<string, object> : null;
            if (beh == null) { beh = new Dictionary<string, object>(); Cfg.Root["behavior"] = beh; }
            beh["autostart"] = on;
            SaveConfig(Cfg.Root);
            SetAutostart(on);
            if (Program.IsTest) Pill.Flash(Loc.Tr("In der Testversion ohne Wirkung"), 3, false);
            else Pill.Flash(on ? Loc.Tr("Nook startet jetzt mit Windows") : Loc.Tr("Nook startet nicht mehr mit Windows"), 3, false);
        }

        // --- Menü, Einstellungen, Reload -----------------------------
        public void ShowMenu(Point screenDip)
        {
            Func<Action, Action> closeFirst = act => () => { ClosePanel(); act(); };
            var items = new List<MI>
            {
                MI.Item(Loc.Tr("Einstellungen"), "\uE713", Loc.Tr("Strg+,"), () => OpenSettings()),
                MI.Item(Loc.Tr("Tastenkürzel"), "\uE765", "F1", OpenHelp),
                MI.Item("Task-Manager", "\uE9D9", Loc.Tr("Strg+Umschalt+Esc"), closeFirst(() => AppIndex.Open("taskmgr.exe"))),
                MI.Sep(),
                MI.Item(Loc.Tr("Config-Ordner öffnen"), "\uE838", closeFirst(OpenConfigFolder)),
                MI.Item(tbHidden ? Loc.Tr("Windows-Taskleiste zeigen") : Loc.Tr("Windows-Taskleiste ausblenden"), "\uE75B", closeFirst(ToggleWindowsTaskbar)),
                MI.Item(Loc.Tr("Neu laden"), "\uE72C", Reload),
                MI.Item(Loc.Tr("Mit Windows starten"), Cfg.B("behavior.autostart", false) ? "\uE73E" : "\uE739", ToggleAutostart),
                MI.Sep(),
                MI.Item(Loc.Tr("Nook beenden"), "\uE7E8", Quit),
                MI.Head("Nook v" + Version + (Program.IsTest ? "  ·  TEST" : ""))
            };
            string otherExe = Store.Str("otherExe", null);
            if (otherExe != null && File.Exists(otherExe))
                items.Insert(items.Count - 2, MI.Item(Program.IsTest ? Loc.Tr("Zur stabilen Version wechseln") : Loc.Tr("Testversion starten"), "\uE8AB", SwitchVersion));
            if (PillPos != null) items.Insert(3, MI.Item(Loc.Tr("Pille an Standardposition"), "\uE81D", () => SetPillPos(null)));
            new MenuWindow(this, items, screenDip).Open();
        }

        public void OpenHelp()
        {
            if (EditorWindow.Current != null) return;
            if (!Panel.IsOpen) TogglePanel("pill");
            if (!Panel.HelpOpen) Panel.ToggleHelp();
        }

        // Ordner mit markierter config.json – zum Sichern oder Teilen
        public void OpenConfigFolder()
        {
            string f = Path.Combine(BaseDir, "config.json");
            try
            {
                if (File.Exists(f)) Process.Start("explorer.exe", "/select,\"" + f + "\"");
                else Process.Start("explorer.exe", "\"" + BaseDir + "\"");
            }
            catch (Exception ex) { Log(ex); }
        }

        // Eingebauter Editor – wird erst beim Öffnen aufgebaut und danach komplett freigegeben
        public void OpenSettings()
        {
            if (EditorWindow.Current != null) { EditorWindow.Current.Activate(); return; }
            Rect from = Panel.IsOpen ? Panel.ShellScreenRect() : PillRect();
            EditorWindow ed = null;
            try
            {
                ed = new EditorWindow(this, from);
                Panel.CloseNow();
                Pill.HideForPanel(true);
                ed.OpenAnimated();
            }
            catch (Exception ex)
            {
                // Editor konnte nicht aufgebaut werden – Nook bleibt normal benutzbar
                Log(ex);
                EditorWindow.Current = null;
                try { if (ed != null) ed.Close(); } catch { }
                EditorWindow.Current = null;
                Pill.HideForPanel(false);
                Pill.Flash(Loc.Tr("Einstellungen: Fehler – Details in data\\error.log"), 6, false);
            }
        }

        public void SaveConfig(Dictionary<string, object> root)
        {
            string target = Path.Combine(BaseDir, "config.json");
            try
            {
                if (File.Exists(target)) File.Copy(target, Path.Combine(DataDir, "config.backup.json"), true);
                File.WriteAllText(target, Json.Write(root), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                Log(ex);
                MessageBox.Show(Loc.Tr("Die Einstellungen konnten nicht gespeichert werden:\n") + ex.Message, "Nook", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Speicher nach dem Editor wieder an Windows zurückgeben
        public static void Trim()
        {
            try
            {
                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                N.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, new IntPtr(-1), new IntPtr(-1));
            }
            catch { }
        }

        // Zusätzliches Tastenkürzel zum Öffnen (behavior.hotkey), z. B. "Ctrl+Alt+Space"
        bool hotkeyOn;
        void ApplyHotkey()
        {
            if (msgWin == null) return;
            if (hotkeyOn) { N.UnregisterHotKey(msgWin.Handle, 0x4E4B); hotkeyOn = false; }
            string hk = Cfg.S("behavior.hotkey", "");
            if (string.IsNullOrEmpty(hk) || hk == "none") return;
            uint mods = 0x4000, vk = 0;      // MOD_NOREPEAT
            foreach (var part0 in hk.Split('+'))
            {
                string part = part0.Trim().ToLowerInvariant();
                if (part == "ctrl" || part == "strg") mods |= 2;
                else if (part == "alt") mods |= 1;
                else if (part == "shift") mods |= 4;
                else if (part == "space") vk = 0x20;
                else if (part.Length == 1) vk = (uint)char.ToUpperInvariant(part[0]);
                else if (part.Length >= 2 && part[0] == 'f') { int fn; if (int.TryParse(part.Substring(1), out fn) && fn >= 1 && fn <= 24) vk = (uint)(0x6F + fn); }
            }
            if (vk == 0) return;
            hotkeyOn = N.RegisterHotKey(msgWin.Handle, 0x4E4B, mods, vk);
            if (!hotkeyOn && Pill != null) Pill.Flash(Loc.Tr("Tastenkürzel ist schon belegt: ") + hk, 4, false);
        }

        public void Reload()
        {
            if (Panel != null) { Panel.CloseNow(); Panel.Close(); }
            if (Pill != null) Pill.Close();
            bool wasHidden = tbHidden;
            LoadConfig();
            Clip.ApplyCfg(Cfg);
            BuildWindows();
            if (!Cfg.B("behavior.hideWindowsTaskbar", true) && wasHidden) RestoreTaskbar();
            ApplySystem();
            ApplyWorkArea();
            ApplyHotkey();
            if (Cfg.Error != null) Pill.Flash(Loc.Tr("Config fehlerhaft – Standardwerte aktiv"), 5, false);
        }

        public void Cleanup()
        {
            if (cleaned) return;
            cleaned = true;
            try { if (hook != null) hook.Uninstall(); } catch { }
            try { if (fgHook != IntPtr.Zero) N.UnhookWinEvent(fgHook); } catch { }
            try { if (msgWin != null) N.RemoveClipboardFormatListener(msgWin.Handle); } catch { }
            try { if (msgWin != null && hotkeyOn) N.UnregisterHotKey(msgWin.Handle, 0x4E4B); } catch { }
            try { RestoreTaskbar(); } catch { }
            try { Clip.SaveNow(); } catch { }
        }

        public void Quit()
        {
            Cleanup();
            Shutdown();
        }

        public static void Log(Exception ex)
        {
            if (ex == null) return;
            try
            {
                string f = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data", "error.log");
                File.AppendAllText(f, DateTime.Now.ToString("s") + "  [v" + Version + "]  " + ex + "\r\n\r\n");
            }
            catch { }
        }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Offene Fenster (für Taskbar-Tab und Pille)
    // -----------------------------------------------------------------
    public class WinItem
    {
        public IntPtr H;
        public string Title, Name, Path, Key;
        public uint Pid;
    }

    public static class WinList
    {
        static readonly Dictionary<uint, string[]> procInfo = new Dictionary<uint, string[]>();
        static readonly Dictionary<IntPtr, ImageSource> winIcons = new Dictionary<IntPtr, ImageSource>();
        static readonly Dictionary<string, ImageSource> pathIcons = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        static readonly uint Me = (uint)Process.GetCurrentProcess().Id;

        // Fenster in Z-Reihenfolge (oberstes zuerst)
        public static List<WinItem> Get()
        {
            var res = new List<WinItem>();
            N.EnumWindows((h, l) =>
            {
                if (!N.IsWindowVisible(h)) return true;
                if (N.GetWindow(h, 4) != IntPtr.Zero) return true;
                int ex = N.GetWindowLong(h, -20);
                if ((ex & 0x80) != 0 && (ex & 0x40000) == 0) return true;
                int cloaked;
                if (N.DwmGetWindowAttribute(h, 14, out cloaked, 4) == 0 && cloaked != 0) return true;
                string title = N.TitleOf(h);
                if (string.IsNullOrWhiteSpace(title)) return true;
                string cls = N.ClassOf(h);
                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" || cls == "Windows.UI.Core.CoreWindow") return true;
                uint pid; N.GetWindowThreadProcessId(h, out pid);
                if (pid == Me) return true;
                var info = Info(pid);
                bool uwp = info[1] == "ApplicationFrameHost";
                res.Add(new WinItem
                {
                    H = h, Title = title, Pid = pid, Path = info[0],
                    Name = uwp ? title : info[1],
                    Key = uwp || string.IsNullOrEmpty(info[0]) ? "hwnd:" + h.ToInt64() : info[0].ToLowerInvariant()
                });
                return true;
            }, IntPtr.Zero);
            if (procInfo.Count > 400) procInfo.Clear();
            if (winIcons.Count > 400) winIcons.Clear();
            return res;
        }

        static string[] Info(uint pid)
        {
            string[] r;
            if (procInfo.TryGetValue(pid, out r)) return r;
            string path = null, name = "";
            try { path = N.ProcessPath(pid); } catch { }
            if (!string.IsNullOrEmpty(path))
            {
                name = System.IO.Path.GetFileNameWithoutExtension(path);
                try
                {
                    var d = FileVersionInfo.GetVersionInfo(path).FileDescription;
                    if (!string.IsNullOrWhiteSpace(d)) name = d.Trim();
                }
                catch { }
                if (System.IO.Path.GetFileNameWithoutExtension(path).Equals("ApplicationFrameHost", StringComparison.OrdinalIgnoreCase)) name = "ApplicationFrameHost";
            }
            r = new[] { path, name };
            procInfo[pid] = r;
            return r;
        }

        public static ImageSource Icon(WinItem w)
        {
            ImageSource ic;
            if (!winIcons.TryGetValue(w.H, out ic))
            {
                ic = N.WindowIcon(w.H);
                winIcons[w.H] = ic;
            }
            if (ic != null || string.IsNullOrEmpty(w.Path)) return ic;
            if (!pathIcons.TryGetValue(w.Path, out ic))
            {
                ic = N.ShellIcon(w.Path, 32);
                pathIcons[w.Path] = ic;
            }
            return ic;
        }

        public static void Activate(IntPtr h)
        {
            if (N.IsIconic(h)) N.ShowWindow(h, 9);
            N.ForceForeground(h);
        }

        public static void Minimize(IntPtr h) { N.ShowWindow(h, 6); }

        public static void Close(IntPtr h) { N.PostMessage(h, 0x10, IntPtr.Zero, IntPtr.Zero); }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Die Pille am Bildschirmrand
    // -----------------------------------------------------------------
    public class PillWindow : Window
    {
        readonly NookApp app;
        readonly Border body, winSep;
        readonly TextBlock clock, date, media, timerTx, flashTx;
        readonly Border dot;
        readonly StackPanel mediaSp, timerSp, winSp;
        StackPanel textItems;
        bool winFirst;
        readonly Btn micBtn;
        const double M = 18;
        readonly double h;
        string flashText;
        DateTime flashUntil;
        bool flashSticky, hiddenForPanel;
        public IntPtr Handle;

        // Ziehen
        bool pressing, dragging;
        Point pressPt;
        Rect pressRect;

        // offene Apps
        readonly List<string> order = new List<string>();
        string winSig = "";
        DispatcherTimer soon;

        public PillWindow(NookApp a)
        {
            app = a;
            var c = a.Cfg;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight; Title = "Nook";
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);

            h = Math.Max(22, c.D("pill.height", 34));
            double fs = c.D("pill.fontSize", 13);
            body = new Border
            {
                Height = h, MinWidth = h * 2, Background = Th.PillBg,
                Padding = new Thickness(h * 0.5, 0, h * 0.5, 0), Cursor = Cursors.Hand
            };
            body.RenderTransform = slide;
            if (c.B("panel.shadow", true))
                body.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = 0.35, Color = Colors.Black };

            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            clock = U.T("", fs, Th.Text, FontWeights.SemiBold);
            date = U.T("", fs, Th.Muted); date.Margin = new Thickness(8, 0, 0, 0);

            timerSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            var ti = U.I("", fs - 1, Th.Accent); ti.Margin = new Thickness(0, 1, 5, 0);
            timerTx = U.T("", fs, Th.Accent, FontWeights.SemiBold);
            timerSp.Children.Add(ti); timerSp.Children.Add(timerTx);

            mediaSp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(12, 0, 0, 0) };
            dot = new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Th.Accent, Margin = new Thickness(0, 1, 7, 0), VerticalAlignment = VerticalAlignment.Center };
            media = U.T("", fs, Th.Text); media.MaxWidth = Math.Max(80, c.D("pill.mediaWidth", 240));
            mediaSp.Children.Add(dot); mediaSp.Children.Add(media);

            flashTx = U.T("", fs, Th.Accent, FontWeights.SemiBold); flashTx.Margin = new Thickness(12, 0, 0, 0);

            winSep = new Border { Width = 1, Height = h * 0.45, Background = Th.Faint, Margin = new Thickness(12, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
            winSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };

            // Mikrofon stumm: rotes Symbol, Klick schaltet das Mikro wieder ein
            micBtn = new Btn(U.I("\uE720", fs, Th.B(Color.FromRgb(240, 90, 80))), Th.B(Color.FromArgb(40, 240, 90, 80)), Th.B(Color.FromArgb(70, 240, 90, 80)), (h - 10) / 2, new Thickness(7, 3, 7, 3));
            micBtn.Visibility = Visibility.Collapsed;
            micBtn.ToolTip = Loc.Tr("Mikrofon stumm – klicken zum Einschalten");
            micBtn.Click += () => { Audio.SetMicMuted(false); app.MicMuted = false; Refresh(); };
            micBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            // Inhalte in der eingestellten Reihenfolge
            var pItems = new Dictionary<string, UIElement> { { "mic", micBtn }, { "clock", clock }, { "date", date }, { "timer", timerSp }, { "media", mediaSp } };
            var pOrder = c.L("pill.order").Select(x => Convert.ToString(x)).ToList();
            foreach (var id in new[] { "mic", "clock", "date", "timer", "media", "windows" }) if (!pOrder.Contains(id)) pOrder.Add(id);
            winFirst = pOrder.IndexOf("windows") == 0;
            foreach (var id in pOrder)
            {
                if (id == "windows")
                {
                    if (winFirst) { sp.Children.Add(winSp); sp.Children.Add(winSep); }
                    else { sp.Children.Add(winSep); sp.Children.Add(winSp); }
                    continue;
                }
                UIElement el;
                if (!pItems.TryGetValue(id, out el) || sp.Children.Contains(el)) continue;
                sp.Children.Add(el);
                if (id == "media") sp.Children.Add(flashTx);
            }
            if (!sp.Children.Contains(flashTx)) sp.Children.Add(flashTx);
            textItems = sp;
            if (winFirst) winSep.Margin = new Thickness(8, 0, 12, 0);
            if (c.B("pill.border", false)) { body.BorderBrush = Th.B(Th.BorderC); body.BorderThickness = new Thickness(Math.Max(1, Th.BorderW)); }
            body.Child = sp;

            var root = new Grid { Margin = new Thickness(M), LayoutTransform = new ScaleTransform(Th.K, Th.K) };
            root.Children.Add(body);
            Content = root;
            ApplyShape();

            SourceInitialized += (s, e) =>
            {
                Handle = new WindowInteropHelper(this).Handle;
                N.AddExStyle(Handle, 0x80 | 0x08000000);
                HwndSource.FromHwnd(Handle).AddHook(WndProc);
            };
            SizeChanged += (s, e) => Reposition();
            body.MouseEnter += (s, e) => body.Background = Th.PillHover;
            body.MouseLeave += (s, e) => { if (!dragging) body.Background = Th.PillBg; };

            body.MouseLeftButtonDown += (s, e) =>
            {
                pressing = true; dragging = false;
                pressPt = ScreenDip(e);
                pressRect = app.PillRect();
                body.CaptureMouse();
                e.Handled = true;
            };
            body.MouseMove += (s, e) =>
            {
                if (!pressing) return;
                var p = ScreenDip(e);
                double dx = p.X - pressPt.X, dy = p.Y - pressPt.Y;
                if (!dragging && app.DragMode != "off" && (Math.Abs(dx) > 5 || Math.Abs(dy) > 5))
                {
                    dragging = true;
                    body.Cursor = Cursors.SizeAll;
                }
                if (dragging)
                {
                    app.PillPos = new Point(pressRect.X + pressRect.Width / 2 + dx, pressRect.Y + dy);
                    ApplyShape();
                    Reposition();
                }
            };
            body.LostMouseCapture += (s, e) => { if (pressing && dragging) EndDrag(); pressing = false; };
            body.MouseLeftButtonUp += (s, e) =>
            {
                if (!pressing) return;
                bool wasDrag = dragging;
                pressing = false;
                if (wasDrag) EndDrag();
                body.ReleaseMouseCapture();
                e.Handled = true;
                if (wasDrag) return;
                if (flashSticky) { flashSticky = false; flashText = null; revealUntil = DateTime.MinValue; Refresh(); return; }
                app.TogglePanel("pill");
            };
            body.MouseRightButtonUp += (s, e) =>
            {
                app.ShowMenu(MenuAnchor(ScreenDip(e)));
                e.Handled = true;
            };
            body.MouseWheel += (s, e) =>
            {
                if (app.Cfg.S("pill.wheel", "volume") != "volume") return;
                double v = Audio.Get();
                if (v < 0) return;
                v = Math.Max(0, Math.Min(1, v + (e.Delta > 0 ? 0.02 : -0.02)));
                Audio.Set(v);
                Flash(Loc.Tr("Lautstärke ") + Math.Round(v * 100) + " %", 1.2, false);
            };
            Closed += (s, e) =>
            {
                if (peek != null) peek.Close();
                if (lip != null) lip.Close();
                if (lipT != null) lipT.Stop();
            };
            Refresh();
            Left = -10000; Top = -10000;
        }

        // Menüs öffnen direkt unter (bzw. über) der Pille – nie dahinter
        public Point MenuAnchor(Point mouse)
        {
            var r = app.PillRect();
            var mon = app.MonitorDip();
            bool below = r.Y + r.Height / 2 < mon.Top + mon.Height / 2;
            return new Point(mouse.X, below ? r.Bottom + 6 : r.Top - 6);
        }

        Point ScreenDip(MouseEventArgs e)
        {
            var p = PointToScreen(e.GetPosition(this));
            double sc = NookApp.Scale();
            return new Point(p.X / sc, p.Y / sc);
        }

        void EndDrag()
        {
            dragging = false;
            body.Cursor = Cursors.Hand;
            if (!body.IsMouseOver) body.Background = Th.PillBg;
            var r = app.PillRect();
            app.SetPillPos(new Point(r.X + r.Width / 2, r.Y));
        }

        IntPtr WndProc(IntPtr hw, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == 0x21) { handled = true; return new IntPtr(3); }           // nicht aktivieren
            return IntPtr.Zero;
        }

        public void ApplyShape()
        {
            double r = h / 2 * Math.Max(0, Math.Min(100, app.Cfg.D("pill.roundness", 100))) / 100.0;
            body.CornerRadius = app.PillPos != null ? new CornerRadius(r) : app.Corners(r);
        }

        public void Reposition()
        {
            var r = app.PillRect();
            Left = Math.Round(r.X - M * Th.K);
            Top = Math.Round(r.Y - M * Th.K);
        }

        bool anyText;

        public void Refresh()
        {
            var c = app.Cfg;
            var now = DateTime.Now;
            micBtn.Visibility = app.MicMuted && c.B("pill.showMicMuted", true) ? Visibility.Visible : Visibility.Collapsed;
            clock.Visibility = c.B("pill.showClock", true) ? Visibility.Visible : Visibility.Collapsed;
            bool sec = c.B("pill.clockSeconds", false);
            clock.Text = now.ToString(c.B("pill.clock24h", true) ? (sec ? "HH:mm:ss" : "HH:mm") : (sec ? "h:mm:ss tt" : "h:mm tt"), Th.Cul);
            date.Visibility = c.B("pill.showDate", false) ? Visibility.Visible : Visibility.Collapsed;
            string df = c.S("pill.dateFormat", "short");
            date.Text = now.ToString(df == "long" ? (Loc.En ? "dddd, MMMM d" : "dddd, d. MMMM") : df == "numeric" ? (Loc.En ? "MM/dd" : "dd.MM.") : Loc.Tr("ddd d. MMM"), Th.Cul);

            bool flash = flashText != null && (flashSticky || now < flashUntil);
            if (!flash) flashText = null;
            flashTx.Text = flash ? flashText : "";
            flashTx.Visibility = flash ? Visibility.Visible : Visibility.Collapsed;

            timerSp.Visibility = !flash && app.Timer.Running ? Visibility.Visible : Visibility.Collapsed;
            timerTx.Text = Countdown.Fmt(app.Timer.Left);

            var mi = app.Media.Info;
            bool showMedia = !flash && c.B("pill.showMedia", true) && mi.Has;
            mediaSp.Visibility = showMedia ? Visibility.Visible : Visibility.Collapsed;
            if (showMedia)
            {
                media.Text = string.IsNullOrEmpty(mi.Artist) ? mi.Title : mi.Title + "  ·  " + mi.Artist;
                media.Foreground = mi.Playing ? Th.Text : Th.Muted;
                dot.Background = mi.Playing ? Th.Accent : Th.Muted;
            }
            // Abstände: erstes sichtbares Element ohne, danach je 12 (Datum direkt hinter der Uhr: 8)
            UIElement prev = null;
            foreach (UIElement el in textItems.Children)
            {
                if (el == winSep || el == winSp) continue;
                var fe = el as FrameworkElement;
                if (fe == null || el.Visibility != Visibility.Visible) continue;
                double left = prev == null ? 0 : (el == date && prev == clock ? 8 : 12);
                fe.Margin = new Thickness(left, 0, 0, 0);
                prev = el;
            }
            anyText = prev != null;
            RefreshWindows();
        }

        // --- offene Apps rechts in der Pille --------------------------
        public void RefreshWindowsSoon()
        {
            if (soon == null)
            {
                soon = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                soon.Tick += (s, e) => { soon.Stop(); RefreshWindows(); };
            }
            soon.Stop(); soon.Start();
        }

        public void RefreshWindows()
        {
            if (!app.Cfg.B("pill.showWindows", true))
            {
                winSp.Visibility = winSep.Visibility = Visibility.Collapsed;
                return;
            }
            var all = WinList.Get();
            var groups = new Dictionary<string, List<WinItem>>();
            foreach (var w in all)
            {
                List<WinItem> l;
                string kk = KeyOf(w);
                if (!groups.TryGetValue(kk, out l)) { l = new List<WinItem>(); groups[kk] = l; }
                l.Add(w);
            }
            order.RemoveAll(k => !groups.ContainsKey(k));
            foreach (var w in all) if (!order.Contains(KeyOf(w))) order.Add(KeyOf(w));

            IntPtr fg = N.GetForegroundWindow();
            string fgKey = all.Where(w => w.H == fg).Select(w => KeyOf(w)).FirstOrDefault();
            int max = Math.Max(1, (int)app.Cfg.D("pill.maxWindowIcons", 12));
            var keys = order.Take(max).ToList();
            string sig = string.Join("|", keys) + "#" + fgKey;
            bool any = keys.Count > 0;
            winSep.Visibility = any && anyText ? Visibility.Visible : Visibility.Collapsed;
            winSp.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            if (sig == winSig) return;
            winSig = sig;
            winSp.Children.Clear();
            double box = h - 6, ic = Math.Round(h * 0.56);
            double icfg = app.Cfg.D("pill.iconSize", 0);
            if (icfg > 0) ic = Math.Max(12, Math.Min(box - 2, icfg));
            foreach (var key in keys)
            {
                var g = groups[key];
                var first = g[0];
                var src = WinList.Icon(first);
                var cell = new Grid { Width = box, Height = box };
                UIElement icon = src != null ? (UIElement)new Image { Source = src, Width = ic, Height = ic } : U.Letter(first.Name, ic);
                ((FrameworkElement)icon).HorizontalAlignment = HorizontalAlignment.Center;
                ((FrameworkElement)icon).VerticalAlignment = VerticalAlignment.Center;
                cell.Children.Add(icon);
                bool active = key == fgKey;
                cell.Children.Add(new Border
                {
                    Width = active ? 10 : 4, Height = 3, CornerRadius = new CornerRadius(1.5),
                    Background = active ? Th.Accent : Th.Muted, VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1)
                });
                var b = new Btn(cell, Brushes.Transparent, Th.Hover, box / 3, new Thickness(0)) { Margin = new Thickness(1, 0, 1, 0) };
                b.ToolTip = g.Count > 1 ? first.Name + " (" + g.Count + " Fenster)" : first.Title;
                string gk = key;
                b.Click += () => { HidePeek(); AppClick(gk); };
                b.MouseUp += (s, e) =>
                {
                    if (e.ChangedButton == MouseButton.Middle && app.Cfg.S("pill.middleClick", "close") == "close") { var ws = Group(gk); if (ws.Count > 0) WinList.Close(ws[0].H); RefreshWindowsSoon(); e.Handled = true; }
                };
                b.MouseRightButtonUp += (s, e) => { AppMenu(gk, MenuAnchor(ScreenDip(e))); e.Handled = true; };
                var bb = b;
                b.MouseEnter += (s, e) => HoverStart(gk, bb);
                winSp.Children.Add(b);
            }
        }

        // Fenster einer App zusammenfassen – oder jedes Fenster einzeln (Einstellung)
        string KeyOf(WinItem w) { return app.Cfg.B("pill.groupWindows", true) ? w.Key : "hwnd:" + w.H.ToInt64(); }

        List<WinItem> Group(string key) { return WinList.Get().Where(w => KeyOf(w) == key).ToList(); }

        void AppClick(string key)
        {
            var ws = Group(key);
            if (ws.Count == 0) return;
            IntPtr fg = N.GetForegroundWindow();
            int i = ws.FindIndex(w => w.H == fg);
            if (i < 0) WinList.Activate(ws[0].H);
            else if (ws.Count > 1) WinList.Activate(ws[ws.Count - 1].H);
            else WinList.Minimize(fg);
            RefreshWindowsSoon();
        }

        void AppMenu(string key, Point at)
        {
            HidePeek();
            var ws = Group(key);
            if (ws.Count == 0) return;
            new MenuWindow(app, AppMenus.ForWindows(app, ws, RefreshWindowsSoon), at).Open();
        }

        // --- Fensterauswahl beim Überfahren ---------------------------
        PeekWindow peek;
        DispatcherTimer peekT;
        string hoverKey;
        Btn hoverBtn;

        DateTime hoverSince, lastOver;

        void HoverStart(string key, Btn b)
        {
            hoverKey = key; hoverBtn = b;
            hoverSince = DateTime.Now; lastOver = DateTime.Now;
            if (peekT == null)
            {
                peekT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                peekT.Tick += (s, e) => PeekTick();
            }
            peekT.Start();
        }

        void PeekTick()
        {
            bool overIcon = hoverBtn != null && hoverBtn.IsMouseOver;
            bool overPeek = peek != null && peek.IsVisible && peek.IsMouseOver;
            if (overIcon || overPeek)
            {
                lastOver = DateTime.Now;
                bool shown = peek != null && peek.IsVisible;
                if (overIcon && (!shown || peek.Key != hoverKey) && (shown || (DateTime.Now - hoverSince).TotalMilliseconds > 350))
                {
                    var ws = Group(hoverKey);
                    if (ws.Count < 2) { if (peek != null) peek.HideNow(); return; }
                    if (peek == null) peek = new PeekWindow(app);
                    var p = hoverBtn.PointToScreen(new Point(0, 0));
                    double sc = NookApp.Scale();
                    peek.ShowFor(hoverKey, ws, new Rect(p.X / sc, p.Y / sc, hoverBtn.ActualWidth, hoverBtn.ActualHeight));
                }
                return;
            }
            if (peek == null || !peek.IsVisible) { peekT.Stop(); return; }
            if ((DateTime.Now - lastOver).TotalMilliseconds > 260) HidePeek();
        }

        public void HidePeek()
        {
            if (peekT != null) peekT.Stop();
            if (peek != null) peek.HideNow();
        }

        public void Flash(string text, double seconds, bool sticky)
        {
            flashText = text; flashSticky = sticky; flashUntil = DateTime.Now.AddSeconds(seconds);
            Refresh();
            if (Mode == "lip" && !IsVisible) { revealUntil = sticky ? DateTime.MaxValue : flashUntil; UpdateVisibility(); }
        }

        public void HideForPanel(bool hide)
        {
            hiddenForPanel = hide;
            if (hide) { HidePeek(); revealed = false; revealUntil = DateTime.MinValue; }
            UpdateVisibility(false);
        }

        // --- Anzeige-Modus: always = immer, lip = Lippe am Rand, hidden = nie --------
        public string Mode
        {
            get
            {
                if (!app.Cfg.B("pill.visible", true)) return "hidden";
                string m = app.Cfg.S("pill.mode", "always");
                return m == "lip" || m == "hidden" ? m : "always";
            }
        }

        bool revealed, animOut;
        DateTime revealUntil = DateTime.MinValue, lastHot;
        LipWindow lip;
        DispatcherTimer lipT;
        readonly TranslateTransform slide = new TranslateTransform();

        bool Wanted()
        {
            if (app.FullscreenActive || hiddenForPanel) return false;
            string m = Mode;
            if (m == "always") return true;
            if (m == "hidden") return false;
            if (flashSticky && flashText != null) return true;
            return revealed || pressing || DateTime.Now < revealUntil;
        }

        public void UpdateVisibility() { UpdateVisibility(true); }

        public void UpdateVisibility(bool animate)
        {
            bool want = Wanted();
            if (want && (!IsVisible || animOut))
            {
                animOut = false;
                ApplyShape();
                if (!IsVisible) { Show(); }
                Reposition(); KeepTopmost();
                SlideIn(animate && Mode == "lip");
            }
            else if (!want && IsVisible && !animOut)
            {
                HidePeek();
                if (animate && Mode == "lip") SlideOut(); else { Hide(); }
            }
            UpdateLip();
        }

        void SlideIn(bool animate)
        {
            double dist = (app.Top ? -1 : 1) * (h + app.Offset / Th.K + 6);
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            if (!animate)
            {
                slide.BeginAnimation(TranslateTransform.YProperty, null); slide.Y = 0;
                body.BeginAnimation(OpacityProperty, null); body.Opacity = 1;
                return;
            }
            slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dist, 0, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160)));
        }

        void SlideOut()
        {
            animOut = true;
            double dist = (app.Top ? -1 : 1) * (h + app.Offset / Th.K + 6);
            var a = new DoubleAnimation(dist, TimeSpan.FromMilliseconds(180)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
            a.Completed += (s, e) => { if (animOut) { animOut = false; Hide(); UpdateLip(); } };
            slide.BeginAnimation(TranslateTransform.YProperty, a);
            body.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(180)));
        }

        void UpdateLip()
        {
            bool lipMode = Mode == "lip";
            if (lipMode && lipT == null)
            {
                lipT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(60) };
                lipT.Tick += (s, e) => LipPoll();
                lipT.Start();
            }
            if (!lipMode && lipT != null) { lipT.Stop(); lipT = null; }
            bool showLip = lipMode && !app.FullscreenActive && !hiddenForPanel && (!IsVisible || animOut);
            if (showLip && lip == null) lip = new LipWindow(app);
            if (lip != null) lip.Set(showLip, app.PillRect());
        }

        // Mauszeiger am Rand? -> Pille zeigen. Weg von der Pille? -> wieder einklappen.
        void LipPoll()
        {
            if (Mode != "lip" || app.FullscreenActive || hiddenForPanel) return;
            NX.POINT p;
            if (!NX.GetCursorPos(out p)) return;
            double sc = NookApp.Scale();
            var c = new Point(p.X / sc, p.Y / sc);
            var r = app.PillRect();
            var mon = app.MonitorDip();
            bool top = app.Top;
            bool inEdge = top ? c.Y <= mon.Top + 3 : c.Y >= mon.Bottom - 4;
            bool inX = c.X >= r.Left - 50 && c.X <= r.Right + 50;
            var now = DateTime.Now;
            if (!revealed && inEdge && inX)
            {
                revealed = true; lastHot = now;
                UpdateVisibility();
                return;
            }
            if (!revealed) return;
            var hot = new Rect(r.X - 24, top ? mon.Top : r.Y - 16, r.Width + 48, top ? r.Bottom + 16 - mon.Top : mon.Bottom - r.Y + 16);
            bool keep = hot.Contains(c) || pressing || (peek != null && peek.IsVisible) || MenuWindow.Current != null;
            if (keep) lastHot = now;
            else if ((now - lastHot).TotalMilliseconds > Math.Max(150, app.Cfg.D("pill.lipHideMs", 650)))
            {
                revealed = false;
                UpdateVisibility();
            }
        }

        public void KeepTopmost()
        {
            if (MenuWindow.Current != null || (peek != null && peek.IsVisible)) return;   // Menüs bleiben vorne
            if (IsVisible && Handle != IntPtr.Zero) N.SetWindowPos(Handle, N.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            if (lip != null) lip.KeepTopmost();
            if (Mode == "lip" && !IsVisible && revealUntil != DateTime.MinValue && DateTime.Now > revealUntil) { revealUntil = DateTime.MinValue; UpdateVisibility(); }
        }

        public Size VisualSize
        {
            get
            {
                double k = Th.K;
                if (body.ActualWidth > 0) return new Size(body.ActualWidth * k, body.ActualHeight * k);
                body.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                if (body.DesiredSize.Width > 0) return new Size(Math.Max(body.DesiredSize.Width, h * 2) * k, h * k);
                return new Size(h * 3.2 * k, h * k);
            }
        }
    }

    // -----------------------------------------------------------------
    //  Kontextmenü (Maus und Tastatur: ↑ ↓ Enter Esc)
    // -----------------------------------------------------------------
    public class MenuWindow : Window
    {
        readonly NookApp app;
        readonly Point anchor;
        bool closing;
        readonly List<Btn> rows = new List<Btn>();
        readonly List<MI> acts = new List<MI>();
        int sel = -1;
        DateTime openedAt;
        Action<IntPtr> fgHandler;
        OutsideClick outside;
        public static MenuWindow Current;

        public MenuWindow(NookApp a, List<KeyValuePair<string, Action>> items, Point anchorDip)
            : this(a, items.Select(kv => kv.Key == null ? MI.Sep() : MI.Item(kv.Key, null, kv.Value)).ToList(), anchorDip) { }

        public MenuWindow(NookApp a, List<MI> items, Point anchorDip)
        {
            app = a; anchor = anchorDip;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
            Title = "Nook Menü";
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            var sp = new StackPanel { MinWidth = 230 };
            bool lastSep = true;
            foreach (var it in items)
            {
                if (it.IsSep)
                {
                    if (lastSep) continue;
                    sp.Children.Add(new Border { Height = 1, Background = Th.Faint, Margin = new Thickness(10, 5, 10, 5) });
                    lastSep = true;
                    continue;
                }
                if (it.Header)
                {
                    var h = U.T(Loc.Tr(it.Text).ToUpper(Th.Cul), 10.5, Th.Muted, FontWeights.Bold);
                    h.Margin = new Thickness(12, sp.Children.Count == 0 ? 4 : 8, 12, 4);
                    sp.Children.Add(h);
                    lastSep = false;
                    continue;
                }
                var g = U.G(null, new[] { U.Px(26), U.Star(1), U.Auto });
                if (!string.IsNullOrEmpty(it.Glyph)) U.Put(g, U.I(it.Glyph, 12, it.Danger ? Th.Accent : Th.Muted), 0, 0);
                var tx = U.T(it.Text, 13, it.Danger ? Th.Accent : Th.Text);
                tx.MaxWidth = 360;
                U.Put(g, tx, 0, 1);
                if (!string.IsNullOrEmpty(it.Key))
                {
                    var k = U.T(it.Key, 11, Th.Muted, FontWeights.SemiBold); k.Margin = new Thickness(18, 0, 0, 0);
                    U.Put(g, k, 0, 2);
                }
                var b = new Btn(g, Brushes.Transparent, Th.Hover, 9, new Thickness(8, 7, 12, 7));
                int idx = rows.Count;
                var mi = it;
                b.Click += () => Run(mi);
                b.MouseEnter += (s2, e2) => SetSel(idx);
                rows.Add(b); acts.Add(it);
                sp.Children.Add(b);
                lastSep = false;
            }
            while (sp.Children.Count > 0 && sp.Children[sp.Children.Count - 1] is Border && !(sp.Children[sp.Children.Count - 1] is Btn))
                sp.Children.RemoveAt(sp.Children.Count - 1);
            var card = new Border
            {
                Background = Th.Bg, CornerRadius = new CornerRadius(14), Padding = new Thickness(6), Margin = new Thickness(16),
                LayoutTransform = new ScaleTransform(Th.K, Th.K),
                Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 560, Focusable = false },
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black }
            };
            Content = card;
            // Schließen nur bei Klick außerhalb des Menüs oder wenn ein anderes Programm
            // den Fokus übernimmt, nachdem das Menü ihn schon hatte
            // Kein Schließen bei Fokusverlust (das machte das Menü unzuverlässig).
            // Es schließt bei Klick außerhalb, Esc, Auswahl – oder wenn du zu einem anderen Programm wechselst.
            fgHandler = hw =>
            {
                if (closing || (DateTime.Now - openedAt).TotalMilliseconds < 600 || hw == IntPtr.Zero) return;
                uint pid; N.GetWindowThreadProcessId(hw, out pid);
                if (pid != (uint)Process.GetCurrentProcess().Id) CloseMenu();
            };
            outside = new OutsideClick(() => CloseMenu(), root => root == new WindowInteropHelper(this).Handle);
            PreviewKeyDown += (s, e) =>
            {
                switch (e.Key)
                {
                    case Key.Escape: case Key.Apps: CloseMenu(); e.Handled = true; break;
                    case Key.Down: SetSel(sel + 1 >= rows.Count ? 0 : sel + 1); e.Handled = true; break;
                    case Key.Up: SetSel(sel <= 0 ? rows.Count - 1 : sel - 1); e.Handled = true; break;
                    case Key.Home: SetSel(0); e.Handled = true; break;
                    case Key.End: SetSel(rows.Count - 1); e.Handled = true; break;
                    case Key.Enter: case Key.Space: if (sel >= 0) Run(acts[sel]); e.Handled = true; break;
                }
            };
            SourceInitialized += (s, e) => N.AddExStyle(new WindowInteropHelper(this).Handle, 0x80);
        }

        void SetSel(int i)
        {
            if (rows.Count == 0) return;
            if (sel >= 0 && sel < rows.Count) rows[sel].SetColors(Brushes.Transparent, Th.Hover);
            sel = Math.Max(0, Math.Min(rows.Count - 1, i));
            rows[sel].SetColors(Th.Hover, Th.Hover);
            rows[sel].BringIntoView();
        }

        void Run(MI it)
        {
            CloseMenu();
            if (it.Do != null) Dispatcher.BeginInvoke(it.Do);
        }

        public void Open()
        {
            if (rows.Count == 0) return;
            if (Current != null) Current.CloseMenu();
            Current = this;
            Left = -10000; Top = -10000;
            Show();
            UpdateLayout();
            var mon = app.MonitorDip();
            double k = Th.K;
            double x = Math.Max(mon.Left + 4, Math.Min(anchor.X - 16 * k, mon.Right - ActualWidth - 4));
            double y = anchor.Y < mon.Top + mon.Height / 2 ? anchor.Y - 16 * k : anchor.Y - ActualHeight + 16 * k;
            y = Math.Max(mon.Top, Math.Min(y, mon.Bottom - ActualHeight));
            Left = x; Top = y;
            Activate();
            N.SetWindowPos(new WindowInteropHelper(this).Handle, N.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            N.ForceForeground(new WindowInteropHelper(this).Handle);
            if (Keyboard.FocusedElement == null || !IsKeyboardFocusWithin) Focus();
            SetSel(0);
            openedAt = DateTime.Now;
            outside.Start();
            NookApp.ForegroundChanged += fgHandler;
        }

        public void CloseMenu()
        {
            if (closing) return;
            closing = true;
            if (outside != null) outside.Stop();
            if (fgHandler != null) NookApp.ForegroundChanged -= fgHandler;
            if (Current == this) Current = null;
            Close();
            if (app.Panel != null && app.Panel.IsOpen) app.Dispatcher.BeginInvoke(new Action(app.Panel.Refocus), DispatcherPriority.Input);
            else if (EditorWindow.Current != null) { var ed = EditorWindow.Current; app.Dispatcher.BeginInvoke(new Action(() => ed.Activate()), DispatcherPriority.Input); }
        }
    }

    // -----------------------------------------------------------------
    //  Das ausklappbare Panel mit Tabs
    // -----------------------------------------------------------------
    public interface ITab
    {
        string Id { get; }
        string Title { get; }
        FrameworkElement View { get; }
        void Shown();
        void Hidden();
        void Key(KeyEventArgs e);
        void Tick();
        bool AutoHeight { get; }
        void FocusMain();
        bool TypeInto(string text);
    }

    public abstract class TabBase : ITab
    {
        protected readonly NookApp App;
        protected TabBase(NookApp app) { App = app; }
        public abstract string Id { get; }
        public abstract string Title { get; }
        public FrameworkElement View { get; protected set; }
        public virtual void Shown() { }
        public virtual void Hidden() { }
        public virtual void Key(KeyEventArgs e) { }
        public virtual void Tick() { }
        public virtual bool AutoHeight { get { return false; } }
        public virtual void FocusMain() { }
        public virtual bool TypeInto(string text) { return false; }
    }

    // Aussehen der Tab-Knöpfe (Stil: Pille / Unterstrich / nur Text; Icons: Text / Icons / beides)
    public static class TabLook
    {
        public static string Glyph(string id)
        {
            switch (id)
            {
                case "controls": return "\uE9E9";
                case "today": return "\uE787";
                case "apps": return "\uE71D";
                case "taskbar": return "\uE7C4";
                case "clipboard": return "\uE77F";
                case "power": return "\uE7E8";
            }
            return "\uE80F";
        }

        public static Btn Make(string id, string title, Cfg c)
        {
            string mode = c.S("panel.tabIcons", "text");
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            if (mode != "text")
            {
                var ic = U.I(Glyph(id), 14, Th.Muted);
                if (mode == "both") ic.Margin = new Thickness(0, 1, 7, 0);
                sp.Children.Add(ic);
            }
            if (mode != "icons") sp.Children.Add(U.T(title, 13.5, Th.Muted, FontWeights.SemiBold));
            var line = new Border { Height = 2.5, CornerRadius = new CornerRadius(1.25), Margin = new Thickness(6, 5, 6, 0), Background = Brushes.Transparent };
            var col = new StackPanel();
            col.Children.Add(sp); col.Children.Add(line);
            var b = new Btn(col, Brushes.Transparent, Th.Surface, 15, new Thickness(mode == "icons" ? 12 : 14, 6, mode == "icons" ? 12 : 14, 6));
            b.Margin = new Thickness(2, 0, 2, 0);
            b.Tag = new object[] { sp, line };
            if (mode == "icons") b.ToolTip = title;
            return b;
        }

        public static void SetOn(Btn b, bool on, Cfg c)
        {
            string st = c.S("panel.tabStyle", "pill");
            var parts = (object[])b.Tag;
            var sp = (StackPanel)parts[0];
            var line = (Border)parts[1];
            var fg = on ? Th.Accent : Th.Muted;
            foreach (var ch in sp.Children) { var t = ch as TextBlock; if (t != null) t.Foreground = fg; }
            if (st == "pill") b.SetColors(on ? Th.AccentSoft : Brushes.Transparent, on ? Th.AccentSoft : Th.Surface);
            else b.SetColors(Brushes.Transparent, st == "text" ? (Brush)Brushes.Transparent : Th.Surface);
            line.Visibility = st == "underline" ? Visibility.Visible : Visibility.Collapsed;
            line.Background = on ? Th.Accent : Brushes.Transparent;
        }
    }

    public class PanelWindow : Window
    {
        readonly NookApp app;
        readonly Border shell, clip, host;
        readonly Grid inner, barGrid;
        readonly TextBlock barClock, barDate;
        readonly Border help;
        bool helpOpen;
        readonly OutsideClick outside;
        Btn gearBtn;
        double curH;
        readonly StackPanel tabSp;
        readonly List<ITab> tabs = new List<ITab>();
        readonly Dictionary<string, Btn> tabBtns = new Dictionary<string, Btn>();
        ITab cur;
        readonly double W, H;
        const double M = 34;
        public bool IsOpen;
        bool animClosing;
        DateTime openedAt;
        IntPtr hwnd;
        Size fromSize;
        double pillRadius, anchorY;
        bool opening, activeSinceOpen;
        DropShadowEffect shadow;
        static double ShadowOpacity { get { return Th.ShadowOpacity; } }
        readonly TranslateTransform innerShift = new TranslateTransform();
        readonly ScaleTransform shellScale = new ScaleTransform();
        readonly TranslateTransform shellMove = new TranslateTransform();
        bool down = true;

        public PanelWindow(NookApp a)
        {
            app = a;
            var c = a.Cfg;
            var mon = a.MonitorDip();
            double k = Th.K;
            W = Math.Min(Math.Max(520, c.D("panel.width", 780)), mon.Width / k - 2 * c.D("position.sideMargin", 16));
            H = Math.Min(Math.Max(360, c.D("panel.height", 470)), mon.Height / k - 80);

            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = true;
            Width = (W + 2 * M) * k; Height = (H + 2 * M) * k; Title = "Nook Panel";
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            FontFamily = Th.Font;

            double r = c.D("panel.radius", 24);
            shell = new Border
            {
                Background = Th.PanelBg, BorderBrush = Th.B(Th.BorderC), BorderThickness = new Thickness(Th.BorderW),
                CornerRadius = a.Corners(r), HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(M)
            };
            var tgrp = new TransformGroup();
            tgrp.Children.Add(shellScale); tgrp.Children.Add(shellMove);
            shell.RenderTransform = tgrp;
            if (c.B("panel.shadow", true))
                shell.Effect = shadow = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = ShadowOpacity, Color = Colors.Black };
            clip = new Border { ClipToBounds = true };
            shell.Child = clip;

            inner = U.G(new[] { U.Auto, U.Star(1) }, null);
            inner.Width = W; inner.Height = H; curH = H;
            inner.RenderTransform = innerShift;
            inner.HorizontalAlignment = HorizontalAlignment.Left;
            inner.VerticalAlignment = VerticalAlignment.Top;
            clip.Child = inner;

            // Tab-Leiste: links Uhrzeit, Mitte Tabs, rechts Hilfe + Menü
            bool tabsLeft = c.S("panel.tabAlign", "center") == "left";
            var bar = barGrid = tabsLeft ? U.G(null, new[] { U.Auto, U.Star(1), U.Auto }) : U.G(null, new[] { U.Star(1), U.Auto, U.Star(1) });
            bar.Margin = new Thickness(22, 16, 18, 14);
            tabSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = tabsLeft ? HorizontalAlignment.Left : HorizontalAlignment.Center };
            if (tabsLeft) tabSp.Margin = new Thickness(14, 0, 0, 0);
            U.Put(bar, tabSp, 0, 1);
            barClock = U.T("", 15, Th.Text, FontWeights.SemiBold);
            barDate = U.T("", 12.5, Th.Muted, FontWeights.SemiBold); barDate.Margin = new Thickness(8, 1, 0, 0);
            var clockSp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            clockSp.Children.Add(barClock); clockSp.Children.Add(barDate);
            clockSp.Visibility = c.B("panel.showClock", true) ? Visibility.Visible : Visibility.Collapsed;
            U.Put(bar, clockSp, 0, 0);
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            var helpBtn = U.IconBtn("\uE897", 32, 14, Brushes.Transparent, Th.Surface, Th.Muted);
            helpBtn.ToolTip = Loc.Tr("Tastenkürzel (F1)");
            helpBtn.Click += () => ToggleHelp();
            var gear = gearBtn = U.IconBtn("\uE713", 32, 14, Brushes.Transparent, Th.Surface, Th.Muted);
            gear.Margin = new Thickness(4, 0, 0, 0);
            gear.Click += () =>
            {
                var p = gear.PointToScreen(new Point(16, down ? 34 : 0));
                double sc = NookApp.Scale();
                app.ShowMenu(new Point(p.X / sc, p.Y / sc));
            };
            if (c.B("panel.showHelpButton", true)) right.Children.Add(helpBtn);
            if (c.B("panel.showMenuButton", true)) right.Children.Add(gear);
            U.Put(bar, right, 0, 2);
            UpdateBarClock();
            U.Put(inner, bar, 0, 0);

            host = new Border { Margin = new Thickness(18, 0, 18, 18) };
            U.Put(inner, host, 1, 0);
            help = new Border { Visibility = Visibility.Collapsed };
            Grid.SetRowSpan(help, 2);
            U.Put(inner, help, 0, 0);
            outside = new OutsideClick(() =>
            {
                if (IsOpen && !animClosing && app.Cfg.B("panel.closeOnBlur", true) && MenuWindow.Current == null) CloseAnimated();
            });

            var root = new Grid { LayoutTransform = new ScaleTransform(k, k) };
            root.Children.Add(shell);
            Content = root;
            BuildTabs();
            shell.MouseRightButtonUp += (s, e) =>
            {
                if (e.Handled || InsideTextBox(e.OriginalSource as DependencyObject)) return;
                var p = PointToScreen(e.GetPosition(this));
                double sc = NookApp.Scale();
                app.ShowMenu(new Point(p.X / sc, p.Y / sc));
                e.Handled = true;
            };

            SourceInitialized += (s, e) => { hwnd = new WindowInteropHelper(this).Handle; N.AddExStyle(hwnd, 0x80); };
            Activated += (s, e) => { if (IsOpen) activeSinceOpen = true; };
            Deactivated += (s, e) =>
            {
                // Hatte das Panel nie den Fokus (z. B. weil ein anderes Programm ihn festhält),
                // schließt es nur per Klick außerhalb oder Esc – nicht sofort wieder.
                if (!activeSinceOpen) return;
                if (!IsOpen || animClosing || !app.Cfg.B("panel.closeOnBlur", true) || (DateTime.Now - openedAt).TotalMilliseconds < 250) return;
                // Wechsel in ein eigenes Fenster (Menü) schließt das Panel nicht
                var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(80) };
                t.Tick += (s2, e2) =>
                {
                    t.Stop();
                    if (!IsOpen || animClosing || IsActive || MenuWindow.Current != null) return;
                    uint pid; N.GetWindowThreadProcessId(N.GetForegroundWindow(), out pid);
                    if (pid == (uint)Process.GetCurrentProcess().Id) return;
                    CloseAnimated();
                };
                t.Start();
            };
            PreviewKeyDown += OnKey;
            PreviewTextInput += OnText;
            // Nach einem Klick ins Panel bleibt die Tastatur aktiv (Pfeiltasten usw.)
            PreviewMouseUp += (s, e) =>
            {
                if (!IsOpen || e.ChangedButton != MouseButton.Left || MenuWindow.Current != null) return;
                if (!IsActive) Activate();
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (IsOpen && MenuWindow.Current == null && IsActive && !(Keyboard.FocusedElement is TextBox) && !IsKeyboardFocusWithin) Keyboard.Focus(this);
                }), DispatcherPriority.Input);
            };
            Left = -10000; Top = -10000;
        }

        void BuildTabs()
        {
            var ids = app.Cfg.L("tabs").Select(x => Convert.ToString(x)).ToList();
            if (ids.Count == 0) ids = new List<string> { "apps" };
            foreach (var id in ids)
            {
                ITab t = null;
                switch (id)
                {
                    case "controls": t = new ControlsTab(app); break;
                    case "today": t = new TodayTab(app); break;
                    case "apps": t = new AppsTab(app); break;
                    case "taskbar": t = new TaskbarTab(app); break;
                    case "clipboard": t = new ClipboardTab(app); break;
                    case "power": t = new PowerTab(app); break;
                }
                if (t == null || tabs.Any(x => x.Id == t.Id)) continue;
                tabs.Add(t);
                var tab = t;
                var b = TabLook.Make(t.Id, t.Title, app.Cfg);
                b.Click += () => Select(tab.Id);
                tabBtns[t.Id] = b;
                tabSp.Children.Add(b);
            }
        }

        static bool InsideTextBox(DependencyObject d)
        {
            while (d != null)
            {
                if (d is TextBox) return true;
                d = (d is Visual || d is System.Windows.Media.Media3D.Visual3D) ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d);
            }
            return false;
        }

        // Höhe eines Tabs: feste Panelhöhe oder – bei AutoHeight – so hoch wie der Inhalt
        double HeightFor(ITab t)
        {
            if (!t.AutoHeight || helpOpen) return H;
            var inf = new Size(W - 36, double.PositiveInfinity);
            t.View.Measure(inf);
            barGrid.Measure(new Size(W, double.PositiveInfinity));
            double h = barGrid.DesiredSize.Height + t.View.DesiredSize.Height + 18;
            return Math.Min(H, Math.Max(180, Math.Ceiling(h)));
        }

        public void Select(string id) { Select(id, true); }

        void Select(string id, bool animate)
        {
            var t = tabs.FirstOrDefault(x => x.Id == id) ?? tabs.FirstOrDefault();
            if (t == null) return;
            if (helpOpen) { helpOpen = false; help.Visibility = Visibility.Collapsed; }
            if (cur != null && cur != t) cur.Hidden();
            cur = t;
            host.Child = t.View;
            foreach (var kv in tabBtns) TabLook.SetOn(kv.Value, kv.Key == t.Id, app.Cfg);
            app.Store.Set("lastTab", t.Id);

            double nh = HeightFor(t);
            if (Math.Abs(nh - curH) > 0.5)
            {
                curH = nh;
                inner.Height = nh;
                if (opening || !IsOpen) shell.Height = nh;
                else if (animate && !animClosing)
                {
                    int ms = (int)Math.Max(1, app.Cfg.D("panel.animationMs", 240) * 0.75);
                    shell.BeginAnimation(HeightProperty, new DoubleAnimation(nh, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                }
            }
            if (IsOpen && !opening)
            {
                Keyboard.Focus(this);
                t.Shown();
            }
        }

        void Step(int d)
        {
            if (tabs.Count == 0) return;
            int i = tabs.IndexOf(cur);
            Select(tabs[((i + d) % tabs.Count + tabs.Count) % tabs.Count].Id);
        }

        // Sichtbare Fläche des Panels in Bildschirm-Koordinaten (DIP)
        public Rect ShellScreenRect()
        {
            try
            {
                var p = shell.PointToScreen(new Point(0, 0));
                double sc = NookApp.Scale();
                return new Rect(p.X / sc, p.Y / sc, W * Th.K, curH * Th.K);
            }
            catch { return app.PillRect(); }
        }

        public bool HelpOpen { get { return helpOpen; } }

        public void ToggleHelp()
        {
            helpOpen = !helpOpen;
            if (helpOpen) help.Child = Shortcuts.Build(() => { if (helpOpen) ToggleHelp(); });
            help.Visibility = helpOpen ? Visibility.Visible : Visibility.Collapsed;
            double nh = cur != null ? HeightFor(cur) : H;
            if (Math.Abs(nh - curH) > 0.5)
            {
                curH = nh; inner.Height = nh;
                int ms = (int)Math.Max(1, app.Cfg.D("panel.animationMs", 240) * 0.75);
                shell.BeginAnimation(HeightProperty, new DoubleAnimation(nh, TimeSpan.FromMilliseconds(ms)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            }
            if (!helpOpen) Refocus();
            else Keyboard.Focus(this);
        }

        public void Refocus()
        {
            if (!IsOpen) return;
            Activate();
            N.ForceForeground(hwnd);
            if (cur != null && !helpOpen) cur.FocusMain(); else Keyboard.Focus(this);
        }

        // Position für ein Menü unter dem Zahnrad (Tastatur)
        public Point GearPoint()
        {
            var p = gearBtn.PointToScreen(new Point(16, down ? 34 : 0));
            double sc = NookApp.Scale();
            return new Point(p.X / sc, p.Y / sc);
        }

        void OnKey(object s, KeyEventArgs e)
        {
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0;

            if (key == Key.F1) { ToggleHelp(); e.Handled = true; return; }
            if (helpOpen)
            {
                if (key == Key.Escape) { ToggleHelp(); e.Handled = true; return; }
                if (key == Key.Left || key == Key.Right) ToggleHelp();
                else if (!ctrl) { e.Handled = true; return; }
            }
            if (ctrl && key == Key.OemComma) { app.OpenSettings(); e.Handled = true; return; }
            if (ctrl && key == Key.Tab)
            {
                Step((mods & ModifierKeys.Shift) != 0 ? -1 : 1);
                e.Handled = true; return;
            }
            if (ctrl && key >= Key.D1 && key <= Key.D9)
            {
                int i = key - Key.D1;
                if (i < tabs.Count) { Select(tabs[i].Id); e.Handled = true; return; }
            }

            // Zuerst darf der Tab reagieren
            if (cur != null) cur.Key(e);
            if (e.Handled) return;

            if (key == Key.Escape)
            {
                var tb = Keyboard.FocusedElement as TextBox;
                if (tb != null && tb.Text.Length > 0) { tb.Text = ""; e.Handled = true; return; }
                CloseAnimated(); e.Handled = true; return;
            }
            if ((key == Key.Left || key == Key.Right) && mods == ModifierKeys.None)
            {
                var tb = Keyboard.FocusedElement as TextBox;
                if (tb == null || tb.Text.Length == 0)
                {
                    Step(key == Key.Left ? -1 : 1);
                    e.Handled = true; return;
                }
            }
            if (key == Key.Apps || (key == Key.F10 && (mods & ModifierKeys.Shift) != 0))
            {
                app.ShowMenu(GearPoint()); e.Handled = true; return;
            }
        }

        public void Open(string tabId)
        {
            if (IsOpen) return;
            IsOpen = true; animClosing = false; activeSinceOpen = false;
            openedAt = DateTime.Now;

            // Geometrie: Panel wächst aus der Pille heraus (Bildschirm-Koordinaten, k = UI-Größe)
            double k = Th.K;
            var pr = app.PillRect();
            var mon = app.MonitorDip();
            double side = app.Side, off = app.Offset;
            double sw = W * k, sh = H * k, sm = M * k;
            bool custom = app.PillPos != null;
            down = custom ? (pr.Y + pr.Height / 2 < mon.Top + mon.Height / 2) : app.Top;
            double px = Math.Max(mon.Left + side, Math.Min(pr.X + pr.Width / 2 - sw / 2, mon.Right - side - sw));
            double winTop;
            if (down) winTop = Math.Min(pr.Y, mon.Bottom - off - sh) - sm;
            else winTop = Math.Max(pr.Bottom, mon.Top + off + sh) + sm - (sh + 2 * sm);
            Left = Math.Round(px - sm); Top = Math.Round(winTop);
            double winBottom = Top + sh + 2 * sm;

            var va = down ? VerticalAlignment.Top : VerticalAlignment.Bottom;
            shell.VerticalAlignment = va; inner.VerticalAlignment = va;
            double r = app.Cfg.D("panel.radius", 24);
            shell.CornerRadius = custom ? new CornerRadius(r) : app.Corners(r);
            fromSize = new Size(pr.Width, pr.Height);
            pillRadius = pr.Height / 2 / k;
            anchorY = down ? Math.Min(pr.Y, mon.Bottom - off - sh) : Math.Max(pr.Bottom, mon.Top + off + sh);
            shell.Margin = down ? new Thickness(M, (anchorY - Top) / k, 0, 0) : new Thickness(M, 0, 0, (winBottom - anchorY) / k);

            if (helpOpen) { helpOpen = false; help.Visibility = Visibility.Collapsed; }
            opening = true;
            UpdateBarClock();
            Select(tabs.Any(x => x.Id == tabId) ? tabId : (cur != null ? cur.Id : tabId), false);

            // feste Endgröße – während der Animation wird nichts neu angeordnet
            shell.BeginAnimation(WidthProperty, null); shell.BeginAnimation(HeightProperty, null); shell.BeginAnimation(MarginProperty, null);
            shell.BeginAnimation(OpacityProperty, null); shell.Opacity = 1;
            shell.Width = W; shell.Height = curH;
            string anim = app.Cfg.S("panel.animStyle", "morph");
            int ms = Math.Max(1, (int)app.Cfg.D("panel.animationMs", 240));
            if (anim == "none" || ms < 10) anim = "none";
            shellScale.BeginAnimation(ScaleTransform.ScaleXProperty, null); shellScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            shellMove.BeginAnimation(TranslateTransform.YProperty, null);
            shellScale.ScaleX = 1; shellScale.ScaleY = 1; shellMove.Y = 0;
            shell.RenderTransformOrigin = new Point(0.5, down ? 0 : 1);
            RectangleGeometry clipGeo = null;
            Rect fromRect = PillInShell(pr);
            if (anim == "morph")
            {
                clipGeo = new RectangleGeometry(fromRect, pillRadius, pillRadius);
                shell.Clip = clipGeo;
                inner.Opacity = 0;
                innerShift.Y = down ? -10 : 10;
            }
            else
            {
                shell.Clip = null;
                inner.BeginAnimation(OpacityProperty, null); inner.Opacity = 1;
                innerShift.BeginAnimation(TranslateTransform.YProperty, null); innerShift.Y = 0;
                if (anim != "none") shell.Opacity = 0;
            }
            if (shadow != null) shadow.Opacity = anim == "none" ? ShadowOpacity : 0;

            Show();
            N.SetWindowPos(hwnd, N.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            Activate();
            N.ForceForeground(hwnd);
            app.OnPanelOpened();
            outside.Start();
            if (cur != null) cur.FocusMain();

            if (anim == "none") { FinishOpen(); return; }
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(ms);
            if (anim == "morph")
            {
                var ra = new RectAnimation(fromRect, new Rect(0, 0, W, curH), dur) { EasingFunction = ease };
                ra.Completed += (s, e) => FinishOpen();
                clipGeo.BeginAnimation(RectangleGeometry.RectProperty, ra);
                var rad = new DoubleAnimation(pillRadius, Math.Min(r, curH / 2), dur) { EasingFunction = ease };
                clipGeo.BeginAnimation(RectangleGeometry.RadiusXProperty, rad);
                clipGeo.BeginAnimation(RectangleGeometry.RadiusYProperty, rad);
                inner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms * 0.7)) { BeginTime = TimeSpan.FromMilliseconds(ms * 0.25) });
                innerShift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, dur) { EasingFunction = ease });
                return;
            }
            // Einblenden (leicht zoomen) oder Gleiten (vom Rand her)
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms * 0.8));
            fadeIn.Completed += (s, e) => FinishOpen();
            shell.BeginAnimation(OpacityProperty, fadeIn);
            if (anim == "fade")
            {
                var sc = new DoubleAnimation(0.94, 1, dur) { EasingFunction = ease };
                shellScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                shellScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
            }
            else shellMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(down ? -28 : 28, 0, dur) { EasingFunction = ease });
        }

        void FinishOpen()
        {
            if (!IsOpen || animClosing) return;
            shell.Clip = null;
            opening = false;
            if (shadow != null) shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(ShadowOpacity, TimeSpan.FromMilliseconds(140)));
            // Inhalte erst nach der Animation auffrischen – kein Ruckeln währenddessen
            if (cur != null) cur.Shown();
        }

        // Pillen-Rechteck in Koordinaten der Panel-Fläche
        Rect PillInShell(Rect pr)
        {
            double k = Th.K;
            double shellX = Left + M * k;
            double shellY = down ? anchorY : anchorY - curH * k;
            return new Rect((pr.X - shellX) / k, (pr.Y - shellY) / k, pr.Width / k, pr.Height / k);
        }

        public void CloseAnimated()
        {
            if (!IsOpen || animClosing) return;
            animClosing = true;
            if (cur != null) cur.Hidden();
            int ms = (int)(app.Cfg.D("panel.animationMs", 240) * 0.75);
            if (ms < 10) { CloseNow(); return; }
            string anim = app.Cfg.S("panel.animStyle", "morph");
            if (anim == "none") { CloseNow(); return; }
            if (anim != "morph")
            {
                if (shadow != null) shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(60)));
                var fo = new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms));
                fo.Completed += (s2, e2) => { if (animClosing) CloseNow(); };
                var ein = new CubicEase { EasingMode = EasingMode.EaseIn };
                if (anim == "fade")
                {
                    var sc = new DoubleAnimation(0.96, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ein };
                    shellScale.BeginAnimation(ScaleTransform.ScaleXProperty, sc);
                    shellScale.BeginAnimation(ScaleTransform.ScaleYProperty, sc);
                }
                else shellMove.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(down ? -20 : 20, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ein });
                shell.BeginAnimation(OpacityProperty, fo);
                return;
            }
            shell.BeginAnimation(HeightProperty, null);
            shell.Height = curH;
            var pr = app.PillRect();
            var target = PillInShell(new Rect(pr.X, pr.Y, fromSize.Width, fromSize.Height));
            var clipGeo = shell.Clip as RectangleGeometry;
            if (clipGeo == null || clipGeo.IsFrozen)
            {
                double r = Math.Min(app.Cfg.D("panel.radius", 24), curH / 2);
                clipGeo = new RectangleGeometry(new Rect(0, 0, W, curH), r, r);
                shell.Clip = clipGeo;
            }
            if (shadow != null) shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(80)));
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(ms);
            inner.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms * 0.45)));
            var ra = new RectAnimation(target, dur) { EasingFunction = ease };
            ra.Completed += (s, e) => { if (animClosing) CloseNow(); };
            clipGeo.BeginAnimation(RectangleGeometry.RectProperty, ra);
            var rad = new DoubleAnimation(pillRadius, dur) { EasingFunction = ease };
            clipGeo.BeginAnimation(RectangleGeometry.RadiusXProperty, rad);
            clipGeo.BeginAnimation(RectangleGeometry.RadiusYProperty, rad);
        }

        public void CloseNow()
        {
            if (!IsOpen) return;
            if (!animClosing && cur != null) cur.Hidden();
            IsOpen = false; animClosing = false; opening = false;
            outside.Stop();
            if (MenuWindow.Current != null) MenuWindow.Current.CloseMenu();
            if (AudioFlyout.Current != null) AudioFlyout.Current.CloseFlyout();
            Hide();
            shell.Clip = null;
            app.OnPanelClosed();
        }

        void UpdateBarClock()
        {
            var now = DateTime.Now;
            barClock.Text = now.ToString(app.Cfg.B("pill.clock24h", true) ? "HH:mm" : "h:mm tt", Th.Cul);
            barDate.Text = now.ToString(Loc.En ? "ddd, MMM d" : "ddd, d. MMM", Th.Cul);
        }

        public void Tick()
        {
            UpdateBarClock();
            if (cur != null) cur.Tick();
            if (IsOpen && IsActive && !IsKeyboardFocusWithin && MenuWindow.Current == null) Keyboard.Focus(this);
        }

        // Einfach lostippen: ohne Suchfeld landet der Text in der App- oder Fenster-Suche
        void OnText(object s, TextCompositionEventArgs e)
        {
            if (!IsOpen || helpOpen || string.IsNullOrEmpty(e.Text)) return;
            if (Keyboard.FocusedElement is TextBox) return;
            char c = e.Text[0];
            if (char.IsControl(c) || char.IsWhiteSpace(c)) return;
            if ((Keyboard.Modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;
            string target = app.Cfg.S("behavior.typeToSearch", "apps");
            if (target == "off") return;
            var t = tabs.FirstOrDefault(x => x.Id == target);
            if (t == null) return;
            if (cur != t) Select(t.Id);
            if (t.TypeInto(e.Text)) e.Handled = true;
        }

        public void MediaChanged()
        {
            var ct = cur as ControlsTab;
            if (ct != null) ct.RefreshMedia();
        }
    }
}

namespace Nook
{
    // Kachel wie im Vorbild (Icon-Kreis, Titel, Untertitel, Pfeil)
    public class Tile : Btn
    {
        readonly TextBlock ic, title, sub, chev;
        readonly Border icBg, sep;
        static readonly SolidColorBrush DarkTint = Th.B(Color.FromArgb(38, 0, 0, 0));

        public Tile(string glyph, string t, bool chevron) : base(null, Th.CardBg, Th.Hover, Th.CardR + 2, new Thickness(12, 0, 12, 0))
        {
            BorderBrush = Th.CardBorder; BorderThickness = new Thickness(Th.CardBorderW);
            var g = U.G(null, new[] { U.Px(38), U.Star(1), U.Auto });
            icBg = new Border { Width = 38, Height = 38, CornerRadius = new CornerRadius(19), Background = Th.Surface2 };
            ic = U.I(glyph, 15, Th.Text);
            icBg.Child = ic;
            U.Put(g, icBg, 0, 0);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            title = U.T(t, 14, Th.Text, FontWeights.SemiBold); title.HorizontalAlignment = HorizontalAlignment.Center;
            sub = U.T("", 12, Th.Muted); sub.HorizontalAlignment = HorizontalAlignment.Center; sub.Margin = new Thickness(0, 1, 0, 0);
            sp.Children.Add(title); sp.Children.Add(sub);
            U.Put(g, sp, 0, 1);
            var right = new StackPanel { Orientation = Orientation.Horizontal, Visibility = chevron ? Visibility.Visible : Visibility.Collapsed };
            sep = new Border { Width = 1, Height = 26, Background = Th.Faint };
            chev = U.I("", 10, Th.Muted); chev.Margin = new Thickness(12, 0, 4, 0);
            right.Children.Add(sep); right.Children.Add(chev);
            U.Put(g, right, 0, 2);
            Child = g;
        }

        public void Set(bool on, string subText)
        {
            SetColors(on ? Th.Accent : Th.CardBg, on ? Th.AccentHover : Th.Hover);
            BorderBrush = on ? Th.Accent : Th.CardBorder;
            title.Foreground = on ? Th.AccentText : Th.Text;
            sub.Foreground = on ? Th.AccentText : Th.Muted;
            sub.Opacity = on ? 0.75 : 1;
            sub.Text = subText;
            icBg.Background = on ? DarkTint : Th.Surface2;
            ic.Foreground = on ? Th.AccentText : Th.Text;
            chev.Foreground = on ? Th.AccentText : Th.Muted;
            sep.Background = on ? DarkTint : Th.Faint;
        }
    }

    // Fortschrittsbalken zum Spulen
    public class SeekBar : Grid
    {
        readonly Border fill, knob;
        double v;
        bool canSeek = true;
        public bool Dragging;
        public event Action<double> Dragged, Seeked;

        public SeekBar()
        {
            Height = 18; Background = Brushes.Transparent; Cursor = Cursors.Hand; VerticalAlignment = VerticalAlignment.Center;
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.Surface2, VerticalAlignment = VerticalAlignment.Center };
            fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Width = 0 };
            knob = new Border { Width = 12, Height = 12, CornerRadius = new CornerRadius(6), Background = Th.Text, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Opacity = 0, IsHitTestVisible = false };
            Children.Add(track); Children.Add(fill); Children.Add(knob);
            SizeChanged += (s, e) => Update();
            MouseEnter += (s, e) => { if (canSeek) knob.Opacity = 1; };
            MouseLeave += (s, e) => { if (!Dragging) knob.Opacity = 0; };
            MouseLeftButtonDown += (s, e) =>
            {
                if (!canSeek) return;
                Dragging = true; CaptureMouse(); FromX(e.GetPosition(this).X); e.Handled = true;
            };
            MouseMove += (s, e) => { if (Dragging) FromX(e.GetPosition(this).X); };
            MouseLeftButtonUp += (s, e) =>
            {
                if (!Dragging) return;
                Dragging = false; ReleaseMouseCapture();
                if (!IsMouseOver) knob.Opacity = 0;
                if (Seeked != null) Seeked(v);
                e.Handled = true;
            };
        }

        public bool CanSeek
        {
            get { return canSeek; }
            set { canSeek = value; Cursor = value ? Cursors.Hand : Cursors.Arrow; if (!value) knob.Opacity = 0; }
        }

        void FromX(double x)
        {
            v = Math.Max(0, Math.Min(1, x / Math.Max(1, ActualWidth)));
            Update();
            if (Dragged != null) Dragged(v);
        }

        public double Value
        {
            get { return v; }
            set { if (Dragging) return; v = Math.Max(0, Math.Min(1, double.IsNaN(value) ? 0 : value)); Update(); }
        }

        void Update()
        {
            double w = ActualWidth;
            fill.Width = Math.Max(0, w * v);
            knob.Margin = new Thickness(Math.Max(0, Math.Min(w - 12, w * v - 6)), 0, 0, 0);
        }
    }

    // -----------------------------------------------------------------
    //  Tab: Steuerung
    // -----------------------------------------------------------------
    public class ControlsTab : TabBase
    {
        public override string Id { get { return "controls"; } }
        public override string Title { get { return Loc.Tr("Steuerung"); } }

        readonly Tile wifi, bt, audio, night, dark, mic;
        static string lastWifi;
        static bool wifiKnown;
        readonly FillSlider vol;
        readonly Border cover;
        readonly TextBlock mTitle, mArtist, mApp, tPos, tDur, playIc;
        readonly SeekBar seek;
        readonly Grid progRow;
        Border mediaCard;
        readonly LinearGradientBrush CoverDefault;

        static LinearGradientBrush MakeCoverBrush()
        {
            var b = new LinearGradientBrush(Th.Mix(Th.AccentC, Colors.Black, 0.35), Th.Mix(Th.AccentC, Color.FromRgb(120, 60, 140), 0.6), 45);
            b.Freeze();
            return b;
        }

        public ControlsTab(NookApp app) : base(app)
        {
            CoverDefault = MakeCoverBrush();
            wifi = new Tile("\uE701", "WLAN", true);
            bt = new Tile("\uE702", "Bluetooth", true);
            audio = new Tile("\uE767", "Audio", true);
            night = new Tile("\uE708", Loc.Tr("Nachtmodus"), true);
            dark = new Tile("\uE793", Loc.Tr("Dunkler Modus"), false);
            mic = new Tile("\uE720", "Mikrofon", false);
            wifi.Click += () => Settings("ms-settings:network-wifi");
            bt.Click += () => Settings("ms-settings:bluetooth");
            audio.Click += OpenAudio;
            night.Click += () => Settings("ms-settings:nightlight");
            dark.Click += ToggleDark;
            mic.Click += ToggleMic;
            var all = new Dictionary<string, KeyValuePair<Tile, Action>>
            {
                { "wifi", new KeyValuePair<Tile, Action>(wifi, () => Settings("ms-settings:network-wifi")) },
                { "bluetooth", new KeyValuePair<Tile, Action>(bt, () => Settings("ms-settings:bluetooth")) },
                { "audio", new KeyValuePair<Tile, Action>(audio, OpenAudio) },
                { "night", new KeyValuePair<Tile, Action>(night, () => Settings("ms-settings:nightlight")) },
                { "dark", new KeyValuePair<Tile, Action>(dark, ToggleDark) },
                { "mic", new KeyValuePair<Tile, Action>(mic, ToggleMic) }
            };
            // Kacheln in der eingestellten Reihenfolge, je 3 pro Reihe
            var ids = app.Cfg.L("controls.tiles").Select(x => Convert.ToString(x)).Where(all.ContainsKey).Distinct().ToList();
            var acts = new List<Action>();
            var rowsDef = new List<GridLength>();
            int tileRows = (ids.Count + 2) / 3;
            for (int i = 0; i < tileRows; i++) { rowsDef.Add(U.Px(62)); rowsDef.Add(U.Px(10)); }
            bool showVol = app.Cfg.B("controls.showVolume", true), showMedia = app.Cfg.B("controls.showMedia", true);
            int volRow = rowsDef.Count;
            if (showVol) { rowsDef.Add(U.Px(48)); rowsDef.Add(U.Px(12)); }
            int mediaRow = rowsDef.Count;
            rowsDef.Add(U.Star(1));
            var g = U.G(rowsDef.ToArray(), null);
            for (int r = 0; r < tileRows; r++)
            {
                var rg = U.G(null, new[] { U.Star(1), U.Px(10), U.Star(1), U.Px(10), U.Star(1) });
                for (int cIdx = 0; cIdx < 3; cIdx++)
                {
                    int i = r * 3 + cIdx;
                    if (i >= ids.Count) break;
                    var kv = all[ids[i]];
                    U.Put(rg, kv.Key, 0, cIdx * 2);
                    acts.Add(kv.Value);
                }
                U.Put(g, rg, r * 2, 0);
            }
            tileActs = acts.ToArray();

            vol = new FillSlider(48);
            vol.Changed += v => { Audio.Set(v); vol.SetIcon(Audio.Glyph(v, false)); };
            vol.IconClick += () => { Audio.ToggleMute(); RefreshVolume(); };
            if (showVol) U.Put(g, vol, volRow, 0);

            // Medien
            var mg = U.G(new[] { U.Auto, U.Px(10), U.Star(1), U.Px(10), U.Auto }, null);
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            var hi = U.I("", 11, Th.Muted); hi.Margin = new Thickness(0, 1, 6, 0);
            mApp = U.T(Loc.Tr("Medien"), 12, Th.Muted, FontWeights.SemiBold);
            head.Children.Add(hi); head.Children.Add(mApp);
            U.Put(mg, head, 0, 0);

            var main = U.G(null, new[] { U.Px(72), U.Px(16), U.Star(1), U.Auto });
            cover = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(12), Background = CoverDefault };
            U.Put(main, cover, 0, 0);
            var txt = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            mTitle = U.T("", 20, Th.Text, FontWeights.SemiBold);
            mArtist = U.T("", 13, Th.Muted); mArtist.Margin = new Thickness(0, 3, 0, 0);
            txt.Children.Add(mTitle); txt.Children.Add(mArtist);
            U.Put(main, txt, 0, 2);
            var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var prev = U.IconBtn("", 40, 13, Th.Surface2, Th.Hover, Th.Text);
            var playColor = Th.B(Th.Mix(Th.TextC, Th.AccentC, 0.12));
            var play = U.IconBtn("", 54, 18, playColor, Th.Text, Th.B(Color.FromRgb(20, 20, 20)));
            playIc = (TextBlock)play.Child;
            var next = U.IconBtn("", 40, 13, Th.Surface2, Th.Hover, Th.Text);
            play.Margin = new Thickness(12, 0, 12, 0);
            prev.Click += () => App.Media.Prev();
            next.Click += () => App.Media.Next();
            playToggle = () =>
            {
                App.Media.Toggle();
                playIc.Text = playIc.Text == "" ? "" : "";
            };
            play.Click += playToggle;
            ctl.Children.Add(prev); ctl.Children.Add(play); ctl.Children.Add(next);
            U.Put(main, ctl, 0, 3);
            U.Put(mg, main, 2, 0);

            progRow = U.G(null, new[] { U.Px(46), U.Star(1), U.Px(46) });
            tPos = U.T("0:00", 11, Th.Muted, FontWeights.SemiBold);
            tDur = U.T("0:00", 11, Th.Muted, FontWeights.SemiBold); tDur.HorizontalAlignment = HorizontalAlignment.Right;
            seek = new SeekBar();
            seek.Dragged += v => { var mi = App.Media.Info; if (mi.Dur > 0) tPos.Text = Time(v * mi.Dur); };
            seek.Seeked += v =>
            {
                var mi = App.Media.Info;
                if (mi.Dur <= 0) return;
                App.Media.Seek(v * mi.Dur);
                tPos.Text = Time(v * mi.Dur);
            };
            U.Put(progRow, tPos, 0, 0); U.Put(progRow, seek, 0, 1); U.Put(progRow, tDur, 0, 2);
            U.Put(mg, progRow, 4, 0);

            var card = new Border { Padding = new Thickness(18, 14, 18, 14), Child = mg };
            Th.StyleCard(card, 2);
            mediaCard = card;
            if (showMedia) U.Put(g, card, mediaRow, 0);
            View = U.Fit(g, 700, 370);
        }

        readonly Action[] tileActs;
        readonly Action playToggle;

        // Audio-Fenster: Ausgabegerät + Lautstärke pro App
        void OpenAudio()
        {
            try
            {
                Rect r;
                if (audio.IsVisible)
                {
                    var p = audio.PointToScreen(new Point(0, 0));
                    double sc = NookApp.Scale();
                    r = new Rect(p.X / sc, p.Y / sc, audio.ActualWidth * Th.K, audio.ActualHeight * Th.K);
                }
                else { var pr = App.Panel.ShellScreenRect(); r = new Rect(pr.X + 20, pr.Y + 40, 60, 40); }
                AudioFlyout.Toggle(App, r);
            }
            catch (Exception ex) { NookApp.Log(ex); }
        }

        void ToggleMic()
        {
            if (!Audio.HasMic()) { App.Pill.Flash(Loc.Tr("Kein Mikrofon gefunden"), 2, false); return; }
            Audio.SetMicMuted(!Audio.MicMuted());
            App.MicMuted = Audio.MicMuted();
            RefreshMic();
            App.Pill.Refresh();
        }

        void RefreshMic()
        {
            bool has = Audio.HasMic();
            bool muted = has && Audio.MicMuted();
            mic.Set(has && !muted, !has ? Loc.Tr("Kein Gerät") : muted ? Loc.Tr("Stumm") : (Audio.MicName() ?? Loc.Tr("An")));
        }

        void ToggleDark()
        {
            bool d = !IsDark();
            dark.Set(d, d ? Loc.Tr("An") : Loc.Tr("Aus"));
            ThreadPool.QueueUserWorkItem(_ => SetDark(d));
        }

        public override void Key(KeyEventArgs e)
        {
            var k = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool shift = mods == ModifierKeys.Shift;
            if (mods == ModifierKeys.None)
            {
                if (k == System.Windows.Input.Key.Up || k == System.Windows.Input.Key.Down)
                {
                    double v = Audio.Get();
                    if (v >= 0) { Audio.Set(v + (k == System.Windows.Input.Key.Up ? 0.05 : -0.05)); RefreshVolume(); }
                    e.Handled = true; return;
                }
                if (k == System.Windows.Input.Key.Space) { playToggle(); e.Handled = true; return; }
            }
            if (mods == ModifierKeys.Alt)
            {
                if (k == System.Windows.Input.Key.N) { App.Media.Next(); e.Handled = true; return; }
                if (k == System.Windows.Input.Key.V) { App.Media.Prev(); e.Handled = true; return; }
                if (k == System.Windows.Input.Key.M) { Audio.ToggleMute(); RefreshVolume(); e.Handled = true; return; }
                int n = -1;
                if (k >= System.Windows.Input.Key.D1 && k <= System.Windows.Input.Key.D6) n = k - System.Windows.Input.Key.D1;
                if (k >= System.Windows.Input.Key.NumPad1 && k <= System.Windows.Input.Key.NumPad6) n = k - System.Windows.Input.Key.NumPad1;
                if (n >= 0 && n < tileActs.Length) { tileActs[n](); e.Handled = true; return; }
            }
            if (shift && (k == System.Windows.Input.Key.Left || k == System.Windows.Input.Key.Right))
            {
                var mi = App.Media.Info;
                if (mi.CanSeek && mi.Dur > 0) App.Media.Seek(Math.Max(0, Math.Min(mi.Dur, mi.Pos + (k == System.Windows.Input.Key.Left ? -10 : 10))));
                e.Handled = true;
            }
        }

        static void Settings(string uri)
        {
            NookApp.I.ClosePanel();
            AppIndex.Open(uri);
        }

        public override void Shown()
        {
            RefreshVolume();
            audio.Set(true, Audio.DeviceName());
            RefreshMic();
            bool d = IsDark();
            dark.Set(d, d ? Loc.Tr("An") : Loc.Tr("Aus"));
            night.Set(false, Loc.Tr("Einstellungen"));
            bt.Set(false, Loc.Tr("Einstellungen"));
            // Letzten bekannten WLAN-Stand sofort zeigen (kein kurzes Grau), dann im Hintergrund prüfen
            if (wifiKnown) wifi.Set(lastWifi != null, lastWifi ?? Loc.Tr("Nicht verbunden"));
            else wifi.Set(false, "…");
            ThreadPool.QueueUserWorkItem(_ =>
            {
                string s = WifiStatus();
                App.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (wifiKnown && s == lastWifi) return;
                    wifiKnown = true; lastWifi = s;
                    wifi.Set(s != null, s ?? Loc.Tr("Nicht verbunden"));
                }));
            });
            RefreshMedia();
        }

        public override void Tick()
        {
            if (!vol.Dragging) RefreshVolume();
        }

        void RefreshVolume()
        {
            double v = Audio.Get();
            bool m = Audio.Muted();
            if (v >= 0) vol.Value = v;
            vol.SetIcon(Audio.Glyph(v, m));
        }

        public void RefreshMedia()
        {
            var mi = App.Media.Info;
            if (mi.Has)
            {
                mTitle.Text = mi.Title;
                mArtist.Text = mi.Artist;
                mApp.Text = string.IsNullOrEmpty(mi.App) ? Loc.Tr("Medien") : mi.App;
                playIc.Text = mi.Playing ? "" : "";
            }
            else
            {
                mTitle.Text = Loc.Tr("Nichts wird abgespielt");
                mArtist.Text = Loc.Tr("Starte Musik in Spotify oder im Browser");
                mApp.Text = App.Media.Rich ? Loc.Tr("Medien") : Loc.Tr("Medien · einfache Variante (ohne Cover und Länge)");
                playIc.Text = "";
            }
            if (mi.Cover != null) cover.Background = new ImageBrush(mi.Cover) { Stretch = Stretch.UniformToFill };
            else cover.Background = CoverDefault;

            bool tl = App.Media.Rich && mi.Has && mi.Dur > 0;
            progRow.Visibility = tl ? Visibility.Visible : Visibility.Collapsed;
            if (tl)
            {
                seek.CanSeek = mi.CanSeek;
                if (!seek.Dragging)
                {
                    tPos.Text = Time(mi.Pos);
                    seek.Value = mi.Pos / mi.Dur;
                }
                tDur.Text = Time(mi.Dur);
            }
        }

        static string Time(double s)
        {
            if (s < 0) s = 0;
            var t = TimeSpan.FromSeconds(s);
            return t.TotalHours >= 1 ? string.Format("{0}:{1:00}:{2:00}", (int)t.TotalHours, t.Minutes, t.Seconds) : string.Format("{0}:{1:00}", t.Minutes, t.Seconds);
        }

        const string PersKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

        static bool IsDark()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(PersKey))
                {
                    var v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 0;
                }
            }
            catch { return true; }
        }

        static void SetDark(bool d)
        {
            try
            {
                using (var k = Registry.CurrentUser.CreateSubKey(PersKey))
                {
                    k.SetValue("AppsUseLightTheme", d ? 0 : 1, RegistryValueKind.DWord);
                    k.SetValue("SystemUsesLightTheme", d ? 0 : 1, RegistryValueKind.DWord);
                }
                IntPtr r;
                N.SendMessageTimeoutStr(new IntPtr(0xFFFF), 0x1A, IntPtr.Zero, "ImmersiveColorSet", 2, 300, out r);
            }
            catch { }
        }

        static string WifiStatus()
        {
            try
            {
                var psi = new ProcessStartInfo("netsh", "wlan show interfaces")
                {
                    UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage)
                };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(3000);
                    var m = Regex.Match(o, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
                    if (m.Success && m.Groups[1].Value.Length > 0) return m.Groups[1].Value;
                }
            }
            catch { }
            try
            {
                foreach (var n in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (n.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                        n.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Ethernet &&
                        n.GetIPProperties().GatewayAddresses.Count > 0)
                        return "Ethernet";
                }
            }
            catch { }
            return null;
        }
    }

    // -----------------------------------------------------------------
    //  Tab: Heute
    // -----------------------------------------------------------------
    public class TodayTab : TabBase
    {
        public override string Id { get { return "today"; } }
        public override string Title { get { return Loc.Tr("Heute"); } }

        readonly TextBlock hh, mm, dateTx, wIcon, wTemp, wDesc, weekRange, weekInfo, ringTx, hNum, mNum, startTx, todoCount;
        readonly Grid weekGrid;
        readonly System.Windows.Shapes.Ellipse ring, todoRing;
        readonly UniformGrid presets;
        readonly StackPanel todoList;
        readonly TextBox todoBox;
        readonly TextBlock todoPh;
        readonly Btn startBtn;
        DateTime weekFor = DateTime.MinValue;
        const double RingD = 84, RingT = 6, TodoD = 30, TodoT = 3;

        public TodayTab(NookApp app) : base(app)
        {
            var g = U.G(null, new[] { U.Star(2.9), U.Px(12), U.Star(4.4), U.Px(12), U.Star(3.7) });

            // Spalte 1: Uhr + Wetter
            var c1 = U.G(new[] { U.Star(1.65), U.Px(12), U.Star(1) }, null);
            var clockSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            hh = U.T("00", 50, Th.Text, FontWeights.Bold); hh.HorizontalAlignment = HorizontalAlignment.Center;
            mm = U.T("00", 50, Th.Text, FontWeights.Bold); mm.HorizontalAlignment = HorizontalAlignment.Center;
            var dots = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 2) };
            dots.Children.Add(Dot(Th.Accent)); dots.Children.Add(Dot(Th.Muted)); dots.Children.Add(Dot(Th.Accent));
            dateTx = U.T("", 13, Th.Muted, FontWeights.SemiBold); dateTx.HorizontalAlignment = HorizontalAlignment.Center; dateTx.Margin = new Thickness(0, 6, 0, 0);
            clockSp.Children.Add(hh); clockSp.Children.Add(dots); clockSp.Children.Add(mm); clockSp.Children.Add(dateTx);
            U.Put(c1, U.Card(clockSp), 0, 0);

            var wSp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            wIcon = U.T("☀", 26, Th.Accent); wIcon.FontFamily = Th.Sym; wIcon.HorizontalAlignment = HorizontalAlignment.Center;
            wTemp = U.T("–", 24, Th.Text, FontWeights.Bold); wTemp.HorizontalAlignment = HorizontalAlignment.Center; wTemp.Margin = new Thickness(0, 2, 0, 0);
            wDesc = U.T("", 12, Th.Muted, FontWeights.SemiBold); wDesc.HorizontalAlignment = HorizontalAlignment.Center;
            wSp.Children.Add(wIcon); wSp.Children.Add(wTemp); wSp.Children.Add(wDesc);
            var wCard = U.Card(wSp);
            U.Put(c1, wCard, 2, 0);
            if (!app.Cfg.B("today.showWeather", true))
            {
                wCard.Visibility = Visibility.Collapsed;
                c1.RowDefinitions[1].Height = U.Px(0); c1.RowDefinitions[2].Height = U.Px(0);
            }
            U.Put(g, c1, 0, 0);

            // Spalte 2: Woche + Timer
            var c2 = U.G(new[] { U.Auto, U.Px(12), U.Star(1) }, null);
            var wk = U.G(new[] { U.Auto, U.Px(12), U.Auto, U.Px(12), U.Auto }, null);
            var wkHead = U.G(null, new[] { U.Star(1), U.Auto });
            U.Put(wkHead, U.T(Loc.Tr("Diese Woche"), 15, Th.Text, FontWeights.SemiBold), 0, 0);
            weekRange = U.Put(wkHead, U.T("", 12, Th.Muted, FontWeights.SemiBold), 0, 1);
            U.Put(wk, wkHead, 0, 0);
            weekGrid = U.G(new[] { U.Auto, U.Px(6), U.Px(40) }, Enumerable.Range(0, 7).Select(i => U.Star(1)).ToArray());
            U.Put(wk, weekGrid, 2, 0);
            var info = new Border { Background = Th.Surface2, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9) };
            var infoG = U.G(null, new[] { U.Px(3), U.Px(10), U.Star(1) });
            U.Put(infoG, new Border { Background = Th.Accent, CornerRadius = new CornerRadius(1.5) }, 0, 0);
            weekInfo = U.Put(infoG, U.T("", 12.5, Th.Text, FontWeights.SemiBold), 0, 2);
            info.Child = infoG;
            U.Put(wk, info, 4, 0);
            U.Put(c2, U.Card(wk), 0, 0);

            // Timer: oben Ring + Stunden/Minuten, darunter Vorgaben, unten Start + Zurücksetzen
            var tg = U.G(new[] { U.Auto, U.Px(10), U.Auto, U.Px(10), U.Auto }, null);
            tg.VerticalAlignment = VerticalAlignment.Center;
            var top = U.G(null, new[] { U.Px(RingD), U.Px(14), U.Star(1) });
            var ringG = new Grid { Width = RingD, Height = RingD, VerticalAlignment = VerticalAlignment.Center };
            ringG.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Th.Surface2, StrokeThickness = RingT });
            ring = new System.Windows.Shapes.Ellipse
            {
                Stroke = Th.Accent, StrokeThickness = RingT, StrokeDashCap = PenLineCap.Round,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90)
            };
            ringG.Children.Add(ring);
            ringTx = U.T("05:00", 17, Th.Text, FontWeights.Bold); ringTx.HorizontalAlignment = HorizontalAlignment.Center;
            ringG.Children.Add(ringTx);
            U.Put(top, ringG, 0, 0);
            var boxes = U.G(new[] { U.Auto, U.Px(8), U.Auto }, null);
            boxes.VerticalAlignment = VerticalAlignment.Center;
            hNum = U.T("0", 16, Th.Text, FontWeights.Bold);
            mNum = U.T("05", 16, Th.Text, FontWeights.Bold);
            U.Put(boxes, NumBox(hNum, "h", 3600), 0, 0);
            U.Put(boxes, NumBox(mNum, "min", 60), 2, 0);
            U.Put(top, boxes, 0, 2);
            U.Put(tg, top, 0, 0);
            presets = new UniformGrid { Rows = 1 };
            U.Put(tg, presets, 2, 0);
            var btns = U.G(null, new[] { U.Star(1), U.Px(8), U.Px(38) });
            startTx = U.T("Start", 13.5, Th.AccentText, FontWeights.SemiBold); startTx.HorizontalAlignment = HorizontalAlignment.Center;
            startBtn = new Btn(startTx, Th.Accent, Th.AccentHover, 12, new Thickness(0, 8, 0, 8));
            startBtn.Click += StartPause;
            startBtn.ToolTip = Loc.Tr("Start / Pause (Alt+S)");
            var reset = new Btn(U.I("\uE72C", 13, Th.Text), Th.Surface2, Th.Hover, 12, new Thickness(0));
            reset.Height = 36;
            reset.ToolTip = Loc.Tr("Zurücksetzen (Alt+R)");
            reset.Click += () => { App.Timer.Reset(); Tick(); };
            U.Put(btns, startBtn, 0, 0); U.Put(btns, reset, 0, 2);
            U.Put(tg, btns, 4, 0);
            var tCard = U.Card(tg);
            U.Put(c2, tCard, 2, 0);
            if (!app.Cfg.B("today.showTimer", true))
            {
                tCard.Visibility = Visibility.Collapsed;
                c2.RowDefinitions[0].Height = U.Star(1); c2.RowDefinitions[1].Height = U.Px(0); c2.RowDefinitions[2].Height = U.Px(0);
            }
            U.Put(g, c2, 0, 2);

            // Spalte 3: To-dos
            var td = U.G(new[] { U.Auto, U.Px(10), U.Star(1), U.Px(10), U.Px(44) }, null);
            var tdHead = U.G(null, new[] { U.Star(1), U.Auto });
            U.Put(tdHead, U.T("To-dos", 15, Th.Text, FontWeights.SemiBold), 0, 0);
            var cg = new Grid { Width = TodoD, Height = TodoD };
            cg.Children.Add(new System.Windows.Shapes.Ellipse { Stroke = Th.Surface2, StrokeThickness = TodoT });
            todoRing = new System.Windows.Shapes.Ellipse
            {
                Stroke = Th.Accent, StrokeThickness = TodoT, StrokeDashCap = PenLineCap.Round,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = new RotateTransform(-90)
            };
            cg.Children.Add(todoRing);
            todoCount = U.T("0/0", 9, Th.Text, FontWeights.Bold); todoCount.HorizontalAlignment = HorizontalAlignment.Center;
            cg.Children.Add(todoCount);
            U.Put(tdHead, cg, 0, 1);
            U.Put(td, tdHead, 0, 0);
            todoList = new StackPanel();
            U.Put(td, U.Scroll(todoList), 2, 0);

            var add = new Grid();
            add.Children.Add(new System.Windows.Shapes.Rectangle
            {
                RadiusX = 12, RadiusY = 12, Stroke = Th.Faint, StrokeThickness = 1.2, StrokeDashArray = new DoubleCollection { 4, 3 }
            });
            var addG = U.G(null, new[] { U.Px(34), U.Star(1), U.Px(38) });
            var plus = U.I("", 12, Th.Muted);
            U.Put(addG, plus, 0, 0);
            var boxG = new Grid();
            todoPh = U.T(Loc.Tr("To-do hinzufügen …"), 13, Th.Muted); todoPh.IsHitTestVisible = false;
            todoBox = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Th.Text, CaretBrush = Th.Accent,
                FontSize = 13, FontFamily = Th.Font, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
                SelectionBrush = Th.Accent, FocusVisualStyle = null, VerticalAlignment = VerticalAlignment.Center
            };
            todoBox.TextChanged += (s, e) => todoPh.Visibility = todoBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            todoBox.KeyDown += (s, e) => { if (e.Key == System.Windows.Input.Key.Enter) { AddTodo(); e.Handled = true; } };
            boxG.Children.Add(todoPh); boxG.Children.Add(todoBox);
            U.Put(addG, boxG, 0, 1);
            var go = U.IconBtn("", 28, 11, Th.Accent, Th.AccentHover, Th.AccentText);
            go.Click += AddTodo;
            U.Put(addG, go, 0, 2);
            add.Children.Add(addG);
            U.Put(td, add, 4, 0);
            var tdCard = U.Card(td);
            U.Put(g, tdCard, 0, 4);
            if (!app.Cfg.B("today.showTodos", true))
            {
                tdCard.Visibility = Visibility.Collapsed;
                g.ColumnDefinitions[3].Width = U.Px(0); g.ColumnDefinitions[4].Width = U.Px(0);
            }

            View = U.Fit(g, 700, 370);
            BuildPresets();
            RenderTodos();
        }

        static Border Dot(Brush b)
        {
            return new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(3.5), Background = b, Margin = new Thickness(3, 0, 3, 0) };
        }

        Border NumBox(TextBlock num, string unit, int step)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var u = U.T(" " + unit, 11, Th.Muted, FontWeights.SemiBold); u.Margin = new Thickness(2, 3, 0, 0);
            sp.Children.Add(num); sp.Children.Add(u);
            var b = new Border { Background = Th.Surface2, CornerRadius = new CornerRadius(11), Height = 38, Child = sp, ToolTip = Loc.Tr("Mausrad zum Einstellen") };
            b.MouseWheel += (s, e) =>
            {
                var t = App.Timer;
                if (t.Running) return;
                int v = t.Total + (e.Delta > 0 ? step : -step);
                if (v < 60) v = 60;
                if (v > 24 * 3600) v = 24 * 3600;
                t.Set(v);
                BuildPresets(); Tick();
                e.Handled = true;
            };
            return b;
        }

        void BuildPresets()
        {
            presets.Children.Clear();
            var list = App.Cfg.L("today.timerPresets");
            if (list.Count == 0) list = new List<object> { 1, 5, 10, 15, 20 };
            var mins = new List<int>();
            foreach (var o in list.Take(5))
            {
                int mm;
                try { mm = Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch { continue; }
                if (mm > 0) mins.Add(mm);
            }
            presets.Columns = Math.Max(1, mins.Count);
            foreach (var o in mins.Cast<object>())
            {
                int min;
                try { min = Convert.ToInt32(o, CultureInfo.InvariantCulture); } catch { continue; }
                if (min <= 0) continue;
                int m = min;
                var chip = U.Chip(m >= 60 && m % 60 == 0 ? (m / 60) + "h" : m + "m", App.Timer.Total == m * 60);
                chip.Margin = new Thickness(2, 0, 2, 0);
                chip.Padding = new Thickness(0, 5, 0, 6);
                ((TextBlock)chip.Child).HorizontalAlignment = HorizontalAlignment.Center;
                chip.Click += () => { App.Timer.Set(m * 60); BuildPresets(); Tick(); };
                presets.Children.Add(chip);
            }
        }

        public override void Shown()
        {
            Tick();
            string city = App.Cfg.S("today.weatherCity", "");
            if (string.IsNullOrWhiteSpace(city))
            {
                wIcon.Text = "☁"; wTemp.Text = "–"; wDesc.Text = Loc.Tr("Ort in der Config setzen");
            }
            else
            {
                RenderWeather();
                if ((DateTime.Now - Weather.Fetched).TotalMinutes > 20)
                    Weather.FetchAsync(city, App.Cfg.S("today.weatherUnit", "C") == "F", RenderWeather);
            }
        }

        void RenderWeather()
        {
            if (double.IsNaN(Weather.Temp)) { wIcon.Text = "☁"; wTemp.Text = "–"; wDesc.Text = Loc.Tr("Lädt …"); return; }
            wIcon.Text = Weather.Symbol(Weather.Code);
            wTemp.Text = Math.Round(Weather.Temp) + (App.Cfg.S("today.weatherUnit", "C") == "F" ? "°F" : "°C");
            wDesc.Text = Weather.Text(Weather.Code);
        }

        public override void Tick()
        {
            var now = DateTime.Now;
            bool h24 = App.Cfg.B("pill.clock24h", true);
            hh.Text = now.ToString(h24 ? "HH" : "hh", Th.Cul);
            mm.Text = now.ToString("mm", Th.Cul);
            dateTx.Text = now.ToString(h24 ? Loc.Tr("ddd, d. MMM") : "tt · ddd d", Th.Cul);
            if (now.Date != weekFor) BuildWeek(now.Date);

            var t = App.Timer;
            int left = t.Left;
            ringTx.Text = Countdown.Fmt(left);
            hNum.Text = (t.Total / 3600).ToString();
            mNum.Text = ((t.Total % 3600) / 60).ToString("00");
            double frac = t.Total > 0 ? (double)left / t.Total : 0;
            SetRing(ring, RingD, RingT, frac);
            startTx.Text = t.Running ? "Pause" : (left < t.Total && left > 0 ? Loc.Tr("Weiter") : "Start");
        }

        static void SetRing(System.Windows.Shapes.Ellipse e, double d, double th, double frac)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            e.Visibility = frac <= 0.001 ? Visibility.Hidden : Visibility.Visible;
            double circ = Math.PI * (d - th) / th;
            e.StrokeDashArray = new DoubleCollection { Math.Max(0, frac * circ - (frac < 1 ? 0.9 : 0)), 1000 };
        }

        void BuildWeek(DateTime today)
        {
            weekFor = today;
            weekGrid.Children.Clear();
            var mon = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
            var names = new[] { "Mo", Loc.Tr("Di"), Loc.Tr("Mi"), Loc.Tr("Do"), "Fr", "Sa", Loc.Tr("So") };
            for (int i = 0; i < 7; i++)
            {
                var d = mon.AddDays(i);
                bool isToday = d == today;
                var n = U.T(names[i], 12, Th.Muted, FontWeights.SemiBold); n.HorizontalAlignment = HorizontalAlignment.Center;
                U.Put(weekGrid, n, 0, i);
                var num = U.T(d.Day.ToString(), 15, isToday ? Th.AccentText : Th.Text, FontWeights.Bold);
                num.HorizontalAlignment = HorizontalAlignment.Center;
                var cell = new Border { Child = num, Margin = new Thickness(2, 0, 2, 0), MaxWidth = 44, CornerRadius = new CornerRadius(11), Background = isToday ? Th.Accent : Brushes.Transparent };
                U.Put(weekGrid, cell, 2, i);
            }
            var sun = mon.AddDays(6);
            if (Loc.En)
                weekRange.Text = mon.Month == sun.Month ? mon.ToString("MMM d", Th.Cul) + "–" + sun.Day : mon.ToString("MMM d", Th.Cul) + " – " + sun.ToString("MMM d", Th.Cul);
            else
                weekRange.Text = mon.Month == sun.Month ? mon.Day + ".–" + sun.ToString("d. MMM", Th.Cul) : mon.ToString("d. MMM", Th.Cul) + " – " + sun.ToString("d. MMM", Th.Cul);
            int kw = Th.Cul.Calendar.GetWeekOfYear(today, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
            int days = DateTime.IsLeapYear(today.Year) ? 366 : 365;
            weekInfo.Text = Loc.Tr("KW ") + kw + Loc.Tr("  ·  Tag ") + today.DayOfYear + Loc.Tr(" von ") + days;
        }

        // --- To-dos ----------------------------------------------
        List<Dictionary<string, object>> Todos()
        {
            object v;
            if (!App.Store.D.TryGetValue("todos", out v)) return new List<Dictionary<string, object>>();
            return Json.Seq(v).OfType<Dictionary<string, object>>().ToList();
        }

        void SaveTodos(List<Dictionary<string, object>> l) { App.Store.Set("todos", l); RenderTodos(); }

        void AddTodo()
        {
            string t = todoBox.Text.Trim();
            if (t.Length == 0) return;
            var l = Todos();
            l.Add(new Dictionary<string, object> { { "t", t }, { "done", false } });
            todoBox.Text = "";
            SaveTodos(l);
        }

        int todoSel = -1;

        void StartPause()
        {
            var t = App.Timer;
            if (t.Running) t.Pause(); else t.Start();
            Tick();
        }

        void ToggleTodo(int idx)
        {
            var cur = Todos();
            if (idx < 0 || idx >= cur.Count) return;
            object d2; bool was = cur[idx].TryGetValue("done", out d2) && d2 is bool && (bool)d2;
            cur[idx]["done"] = !was;
            SaveTodos(cur);
        }

        public override void Hidden() { todoSel = -1; }

        public override void Key(KeyEventArgs e)
        {
            var k = e.Key;
            if (todoBox.IsKeyboardFocused)
            {
                if (k == System.Windows.Input.Key.Escape) { todoBox.Text = ""; Keyboard.Focus(App.Panel); e.Handled = true; }
                else if (k == System.Windows.Input.Key.Up || k == System.Windows.Input.Key.Down)
                {
                    Keyboard.Focus(App.Panel);
                    int n = Todos().Count;
                    if (n > 0) { todoSel = k == System.Windows.Input.Key.Up ? n - 1 : 0; RenderTodos(); }
                    e.Handled = true;
                }
                return;
            }
            if (Keyboard.Modifiers == ModifierKeys.Alt)
            {
                var sk = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
                if (sk == System.Windows.Input.Key.S) { StartPause(); e.Handled = true; }
                else if (sk == System.Windows.Input.Key.R) { App.Timer.Reset(); Tick(); e.Handled = true; }
                else if (sk == System.Windows.Input.Key.N) { todoSel = -1; RenderTodos(); todoBox.Focus(); Keyboard.Focus(todoBox); e.Handled = true; }
                return;
            }
            if (Keyboard.Modifiers != ModifierKeys.None) return;
            int count = Todos().Count;
            switch (k)
            {
                case System.Windows.Input.Key.OemPlus: case System.Windows.Input.Key.Add:
                case System.Windows.Input.Key.OemMinus: case System.Windows.Input.Key.Subtract:
                    if (!App.Timer.Running)
                    {
                        bool plus = k == System.Windows.Input.Key.OemPlus || k == System.Windows.Input.Key.Add;
                        App.Timer.Set(Math.Max(60, Math.Min(24 * 3600, App.Timer.Total + (plus ? 60 : -60))));
                        BuildPresets(); Tick();
                    }
                    e.Handled = true; break;
                case System.Windows.Input.Key.Down:
                    if (count > 0) { todoSel = todoSel < 0 ? 0 : Math.Min(count - 1, todoSel + 1); RenderTodos(); }
                    e.Handled = true; break;
                case System.Windows.Input.Key.Up:
                    if (count > 0) { todoSel = todoSel < 0 ? count - 1 : Math.Max(0, todoSel - 1); RenderTodos(); }
                    e.Handled = true; break;
                case System.Windows.Input.Key.Space:
                case System.Windows.Input.Key.Enter:
                    if (todoSel >= 0) { ToggleTodo(todoSel); e.Handled = true; }
                    break;
                case System.Windows.Input.Key.Delete:
                    if (todoSel >= 0 && todoSel < count)
                    {
                        var cur = Todos(); cur.RemoveAt(todoSel);
                        todoSel = Math.Min(todoSel, cur.Count - 1);
                        SaveTodos(cur);
                        e.Handled = true;
                    }
                    break;
            }
        }

        void RenderTodos()
        {
            todoList.Children.Clear();
            var l = Todos();
            int done = 0;
            for (int i = 0; i < l.Count; i++)
            {
                var item = l[i];
                object dv; bool isDone = item.TryGetValue("done", out dv) && dv is bool && (bool)dv;
                if (isDone) done++;
                int idx = i;
                var rg = U.G(null, new[] { U.Px(28), U.Star(1), U.Px(26) });
                var check = new Border
                {
                    Width = 18, Height = 18, CornerRadius = new CornerRadius(9),
                    Background = isDone ? Th.Accent : Brushes.Transparent, BorderBrush = isDone ? Th.Accent : Th.Muted, BorderThickness = new Thickness(1.5),
                    Child = isDone ? U.I("", 9, Th.AccentText) : null, HorizontalAlignment = HorizontalAlignment.Left
                };
                U.Put(rg, check, 0, 0);
                var tx = U.T(Convert.ToString(item["t"]), 13, isDone ? Th.Muted : Th.Text, FontWeights.SemiBold);
                if (isDone) tx.TextDecorations = TextDecorations.Strikethrough;
                U.Put(rg, tx, 0, 1);
                var del = U.IconBtn("", 22, 9, Brushes.Transparent, Th.Hover, Th.Muted);
                del.HorizontalAlignment = HorizontalAlignment.Right;
                del.Click += () => { var cur = Todos(); if (idx < cur.Count) { cur.RemoveAt(idx); SaveTodos(cur); } };
                U.Put(rg, del, 0, 2);
                bool selRow = i == todoSel;
                var row = new Btn(rg, selRow ? Th.AccentSoft : Th.Surface2, selRow ? Th.AccentSoft : Th.Hover, 12, new Thickness(10, 8, 6, 8)) { Margin = new Thickness(0, 0, 0, 6) };
                row.Click += () =>
                {
                    var cur = Todos();
                    if (idx >= cur.Count) return;
                    object d2; bool was = cur[idx].TryGetValue("done", out d2) && d2 is bool && (bool)d2;
                    cur[idx]["done"] = !was;
                    SaveTodos(cur);
                };
                todoList.Children.Add(row);
                if (selRow) row.Loaded += (s2, e2) => row.BringIntoView();
            }
            if (l.Count == 0)
            {
                var e = U.T(Loc.Tr("Nichts zu tun. Schön."), 13, Th.Muted); e.Margin = new Thickness(4, 6, 0, 0);
                todoList.Children.Add(e);
            }
            todoCount.Text = done + "/" + l.Count;
            SetRing(todoRing, TodoD, TodoT, l.Count == 0 ? 0 : (double)done / l.Count);
        }
    }
}

namespace Nook
{
    // Suchfeld mit Platzhalter
    public class SearchBox : Border
    {
        public readonly TextBox Tb;
        readonly TextBlock ph;
        TextBlock hintTb;

        public void SetHint(string t) { if (hintTb != null) hintTb.Text = t; }

        public SearchBox(string placeholder, string hint, double size, bool background)
        {
            if (background) Th.StyleCard(this, -4); else Background = Brushes.Transparent;
            if (!background) CornerRadius = new CornerRadius(14);
            Padding = new Thickness(16, 0, 14, 0);
            var g = U.G(null, new[] { U.Px(30), U.Star(1), U.Auto });
            var ic = U.I("", size * 0.95, Th.Muted); ic.HorizontalAlignment = HorizontalAlignment.Left;
            U.Put(g, ic, 0, 0);
            var inner = new Grid();
            ph = U.T(placeholder, size, Th.Muted); ph.IsHitTestVisible = false;
            Tb = new TextBox
            {
                Background = Brushes.Transparent, BorderThickness = new Thickness(0), Foreground = Th.Text, CaretBrush = Th.Accent,
                FontSize = size, FontFamily = Th.Font, VerticalContentAlignment = VerticalAlignment.Center, Padding = new Thickness(0),
                SelectionBrush = Th.Accent, FocusVisualStyle = null, VerticalAlignment = VerticalAlignment.Center
            };
            Tb.TextChanged += (s, e) => ph.Visibility = Tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            inner.Children.Add(ph); inner.Children.Add(Tb);
            U.Put(g, inner, 0, 1);
            if (hint != null)
            {
                hintTb = U.T(hint, 11.5, Th.Muted, FontWeights.SemiBold); hintTb.Margin = new Thickness(10, 0, 0, 0);
                U.Put(g, hintTb, 0, 2);
            }
            Child = g;
            Cursor = Cursors.IBeam;
            MouseLeftButtonDown += (s, e) => FocusBox();
        }

        public void FocusBox()
        {
            Tb.Focus();
            Keyboard.Focus(Tb);
        }
    }

    // Liste mit Tastaturauswahl (Apps, Fenster, Zwischenablage)
    public class SelList
    {
        public readonly StackPanel Panel = new StackPanel();
        public readonly ScrollViewer Scroll;
        readonly List<Btn> rows = new List<Btn>();
        readonly List<Action> acts = new List<Action>();
        readonly List<Brush> normal = new List<Brush>();
        public int Sel = -1;
        public event Action<int> SelChanged;
        public event Action<int, Point> ContextRequested;

        public SelList() { Scroll = U.Scroll(Panel); }

        public void Clear() { Panel.Children.Clear(); rows.Clear(); acts.Clear(); normal.Clear(); Sel = -1; }

        public void Add(UIElement e) { Panel.Children.Add(e); }

        public int AddRow(Btn b, Action act, Brush bg)
        {
            int idx = rows.Count;
            rows.Add(b); acts.Add(act); normal.Add(bg);
            b.Click += () => { Select(idx); if (act != null) act(); };
            b.MouseRightButtonUp += (s, e) =>
            {
                Select(idx);
                if (ContextRequested != null)
                {
                    var p = b.PointToScreen(e.GetPosition(b));
                    double sc = NookApp.Scale();
                    ContextRequested(idx, new Point(p.X / sc, p.Y / sc));
                }
                e.Handled = true;
            };
            Panel.Children.Add(b);
            return idx;
        }

        public int Count { get { return rows.Count; } }

        public void Select(int i)
        {
            if (rows.Count == 0) { Sel = -1; return; }
            i = Math.Max(0, Math.Min(rows.Count - 1, i));
            if (Sel >= 0 && Sel < rows.Count) rows[Sel].SetColors(normal[Sel], Th.Surface);
            Sel = i;
            rows[i].SetColors(Th.AccentSoft, Th.AccentSoft);
            rows[i].BringIntoView();
            if (SelChanged != null) SelChanged(i);
        }

        // Bildschirmposition unter der Auswahl (für Menüs per Tastatur)
        public Point SelPoint()
        {
            if (Sel < 0 || Sel >= rows.Count) return new Point();
            var r = rows[Sel];
            var p = r.PointToScreen(new Point(40, r.ActualHeight));
            double sc = NookApp.Scale();
            return new Point(p.X / sc, p.Y / sc);
        }

        public void Move(int d) { if (rows.Count > 0) Select(Sel < 0 ? 0 : Sel + d); }

        public void Run() { if (Sel >= 0 && Sel < acts.Count && acts[Sel] != null) acts[Sel](); }

        public static Btn Row(UIElement content, double height)
        {
            var b = new Btn(content, Brushes.Transparent, Th.Surface, 12, new Thickness(10, 0, 10, 0));
            b.Height = height;
            b.Margin = new Thickness(0, 0, 0, 2);
            return b;
        }
    }

    // -----------------------------------------------------------------
    //  Tab: Apps
    // -----------------------------------------------------------------
    public class AppsTab : TabBase
    {
        public override string Id { get { return "apps"; } }
        public override string Title { get { return "Apps"; } }

        readonly SearchBox sb;
        readonly WrapPanel chips;
        readonly SelList list = new SelList();
        readonly List<AppEntry> rowApps = new List<AppEntry>();
        List<AppEntry> pending = new List<AppEntry>();
        int pendingAt;
        HashSet<string> starred = new HashSet<string>();
        bool suppress;
        const int Page = 30;

        public AppsTab(NookApp app) : base(app)
        {
            var g = U.G(new[] { U.Px(54), U.Auto, U.Px(8), U.Star(1) }, null);
            sb = new SearchBox(Loc.Tr("Apps durchsuchen oder rechnen …"), "Win", 16, true);
            U.Put(g, sb, 0, 0);
            chips = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
            U.Put(g, chips, 1, 0);
            U.Put(g, list.Scroll, 3, 0);
            View = g;

            sb.Tb.TextChanged += (s, e) => { if (!suppress) Rebuild(); };
            list.Scroll.ScrollChanged += (s, e) =>
            {
                if (pendingAt < pending.Count && e.VerticalOffset + e.ViewportHeight > e.ExtentHeight - 160) LoadMore();
            };
            list.ContextRequested += (i, p) => ShowMenu(i, p);
            App.Apps.Loaded += () => { if (App.Panel != null && App.Panel.IsOpen) Rebuild(); };
            BuildChips();
        }

        void BuildChips()
        {
            chips.Children.Clear();
            foreach (var o in App.Cfg.L("apps.quickLinks"))
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                object nv, tv;
                if (!d.TryGetValue("name", out nv) || !d.TryGetValue("target", out tv)) continue;
                string name = Convert.ToString(nv), target = Convert.ToString(tv);
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(target)) continue;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                var ic = new Border { Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Th.AccentSoft, Child = U.I("", 10, Th.Accent), Margin = new Thickness(0, 0, 8, 0) };
                sp.Children.Add(ic);
                sp.Children.Add(U.T(name, 13, Th.Text, FontWeights.SemiBold));
                var b = new Btn(sp, Th.Surface, Th.Hover, 16, new Thickness(6, 5, 14, 5)) { Margin = new Thickness(0, 0, 8, 6) };
                b.Click += () => { App.ClosePanel(); AppIndex.Open(target); };
                chips.Children.Add(b);
            }
            chips.Visibility = chips.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        public override void Shown()
        {
            if (sb.Tb.Text.Length == 0) Rebuild();
            FocusMain();
        }

        public override void Hidden() { suppress = true; sb.Tb.Text = ""; suppress = false; }

        public override void FocusMain()
        {
            App.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(sb.FocusBox));
        }

        public override bool TypeInto(string text)
        {
            sb.FocusBox();
            sb.Tb.Text += text;
            sb.Tb.CaretIndex = sb.Tb.Text.Length;
            return true;
        }

        AppEntry SelApp { get { return list.Sel >= 0 && list.Sel < rowApps.Count ? rowApps[list.Sel] : null; } }

        public override void Key(KeyEventArgs e)
        {
            var k = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;
            var a = SelApp;
            if (k == System.Windows.Input.Key.Down) { if (list.Sel >= list.Count - 3) LoadMore(); list.Move(1); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Up) { list.Move(-1); e.Handled = true; }
            else if (k == System.Windows.Input.Key.PageDown) { LoadMore(); list.Move(8); e.Handled = true; }
            else if (k == System.Windows.Input.Key.PageUp) { list.Move(-8); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Enter && shift && a != null)
            {
                var t = AppMenus.TargetOf(a);
                if (t != null) { App.ClosePanel(); AppMenus.RunAsAdmin(t); } else App.Pill.Flash(Loc.Tr("Als Admin hier nicht möglich"), 2, false);
                e.Handled = true;
            }
            else if (k == System.Windows.Input.Key.Enter) { list.Run(); e.Handled = true; }
            else if (ctrl && k == System.Windows.Input.Key.O && a != null)
            {
                var t = AppMenus.TargetOf(a);
                if (t != null) { App.ClosePanel(); AppMenus.ShowInFolder(t); }
                e.Handled = true;
            }
            else if (ctrl && k == System.Windows.Input.Key.S && a != null) { ToggleStar(a); e.Handled = true; }
            else if ((k == System.Windows.Input.Key.Apps || (k == System.Windows.Input.Key.F10 && shift)) && list.Sel >= 0)
            {
                ShowMenu(list.Sel, list.SelPoint()); e.Handled = true;
            }
            else if (!sb.Tb.IsKeyboardFocused && k != System.Windows.Input.Key.Tab) sb.FocusBox();
        }

        void ShowMenu(int i, Point at)
        {
            if (i < 0 || i >= rowApps.Count || rowApps[i] == null) return;
            var a = rowApps[i];
            new MenuWindow(App, AppMenus.ForApp(App, a, () => Launch(a), () => ToggleStar(a), starred.Contains(a.Parse)), at).Open();
        }

        void ToggleStar(AppEntry a)
        {
            var l = App.Store.Strs("starred");
            if (l.Contains(a.Parse)) l.Remove(a.Parse); else l.Add(a.Parse);
            App.Store.Set("starred", l);
            int keep = list.Sel;
            Rebuild();
            list.Select(keep);
        }

        void Rebuild()
        {
            list.Clear(); rowApps.Clear();
            pending = new List<AppEntry>(); pendingAt = 0;
            string q = sb.Tb.Text.Trim().ToLowerInvariant();
            var all = App.Apps.All;
            starred = new HashSet<string>(App.Store.Strs("starred"));

            if (q.Length == 0)
            {
                var fav = App.Cfg.B("apps.showFavorites", true) ? all.Where(a => starred.Contains(a.Parse)).ToList() : new List<AppEntry>();
                if (fav.Count > 0) { list.Add(U.Section(Loc.Tr("Favoriten"))); foreach (var a in fav) AddApp(a); }
                int recN = Math.Max(0, Math.Min(15, (int)App.Cfg.D("apps.recentCount", 5)));
                var rec = App.Store.Strs("recent").Select(p => App.Apps.Find(p)).Where(a => a != null && !fav.Contains(a)).Take(recN).ToList();
                if (rec.Count > 0) { list.Add(U.Section(Loc.Tr("Zuletzt"))); foreach (var a in rec) AddApp(a); }
                list.Add(U.Section(Loc.Tr("Alle Apps")));
                if (all.Count == 0) list.Add(new Border { Margin = new Thickness(12, 4, 0, 0), Child = U.T(Loc.Tr("Apps werden geladen …"), 13, Th.Muted) });
                pending = all;
                LoadMore();
            }
            else
            {
                double r;
                if (Calc.TryEval(sb.Tb.Text, out r)) AddCalc(r);
                var hits = all.Select(a => new { a, s = AppIndex.Score(a.Name, q) + (starred.Contains(a.Parse) ? 50 : 0) })
                              .Where(x => x.s > 0).OrderByDescending(x => x.s).Take(30).Select(x => x.a).ToList();
                foreach (var a in hits) AddApp(a);
                AddWeb(sb.Tb.Text.Trim());
            }
            if (list.Count > 0) list.Select(0);
            list.Scroll.ScrollToTop();
        }

        // Lange Liste in Häppchen aufbauen – vermeidet das Ruckeln beim Öffnen
        void LoadMore()
        {
            int end = Math.Min(pending.Count, pendingAt + Page);
            for (; pendingAt < end; pendingAt++) AddApp(pending[pendingAt]);
        }

        void AddApp(AppEntry a)
        {
            bool compact = App.Cfg.B("apps.compact", false);
            double isz = compact ? 22 : 28;
            var g = U.G(null, new[] { U.Px(isz + 4), U.Px(12), U.Star(1), U.Auto, U.Px(6), U.Px(30) });
            var iconHost = new Border { Width = isz, Height = isz };
            Action setIcon = () =>
            {
                iconHost.Child = a.Icon != null
                    ? (UIElement)new Image { Source = a.Icon, Width = isz, Height = isz }
                    : U.Letter(a.Name, isz);
            };
            setIcon();
            a.OnIcon = setIcon;
            a.RequestIcon();
            U.Put(g, iconHost, 0, 0);
            U.Put(g, U.T(a.Name, compact ? 13.5 : 14.5, Th.Text, FontWeights.SemiBold), 0, 2);
            if (App.Cfg.B("apps.showKind", true)) U.Put(g, U.T(a.Kind, 12, Th.Muted, FontWeights.SemiBold), 0, 3);
            bool st = starred.Contains(a.Parse);
            var star = U.IconBtn(st ? "" : "", 28, 13, Brushes.Transparent, Th.Hover, st ? Th.Accent : Th.Faint);
            star.Click += () => ToggleStar(a);
            U.Put(g, star, 0, 5);
            rowApps.Add(a);
            list.AddRow(SelList.Row(g, compact ? 36 : 46), () => Launch(a), Brushes.Transparent);
        }

        void Launch(AppEntry a)
        {
            var rec = App.Store.Strs("recent");
            rec.Remove(a.Parse); rec.Insert(0, a.Parse);
            App.Store.Set("recent", rec.Take(12).ToList());
            App.ClosePanel();
            AppIndex.Launch(a);
        }

        void AddCalc(double r)
        {
            var g = U.G(null, new[] { U.Px(32), U.Px(12), U.Star(1), U.Auto });
            var ic = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Th.AccentSoft, Child = U.I("", 13, Th.Accent) };
            U.Put(g, ic, 0, 0);
            U.Put(g, U.T("= " + Calc.Format(r), 18, Th.Text, FontWeights.Bold), 0, 2);
            U.Put(g, U.T(Loc.Tr("Enter kopiert"), 12, Th.Muted, FontWeights.SemiBold), 0, 3);
            rowApps.Add(null);
            list.AddRow(SelList.Row(g, 50), () =>
            {
                try { Clipboard.SetText(Calc.Plain(r)); } catch { }
                App.Pill.Flash(Loc.Tr("Ergebnis kopiert"), 1.5, false);
                App.ClosePanel();
            }, Brushes.Transparent);
        }

        void AddWeb(string q)
        {
            string url = App.Cfg.S("apps.webSearch", "https://www.google.com/search?q=");
            if (string.IsNullOrWhiteSpace(url)) return;
            var g = U.G(null, new[] { U.Px(32), U.Px(12), U.Star(1), U.Auto });
            var ic = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Th.Surface2, Child = U.I("", 13, Th.Text) };
            U.Put(g, ic, 0, 0);
            U.Put(g, U.T(Loc.Tr("„") + q + Loc.Tr("“ im Web suchen"), 14.5, Th.Text, FontWeights.SemiBold), 0, 2);
            U.Put(g, U.T("Web", 12, Th.Muted, FontWeights.SemiBold), 0, 3);
            rowApps.Add(null);
            list.AddRow(SelList.Row(g, 46), () => { App.ClosePanel(); AppIndex.Open(url + Uri.EscapeDataString(q)); }, Brushes.Transparent);
        }
    }

    // -----------------------------------------------------------------
    //  Tab: Taskbar (laufende Fenster, nach App sortiert, durchsuchbar)
    // -----------------------------------------------------------------
    public class TaskbarTab : TabBase
    {
        public override string Id { get { return "taskbar"; } }
        public override string Title { get { return "Taskbar"; } }

        readonly SearchBox sb;
        readonly SelList list = new SelList();
        List<WinItem> shown = new List<WinItem>();
        string sig = "";
        bool suppress;

        public TaskbarTab(NookApp app) : base(app)
        {
            var g = U.G(new[] { U.Px(54), U.Px(10), U.Star(1) }, null);
            sb = new SearchBox(Loc.Tr("Offene Fenster durchsuchen …"), "", 16, true);
            U.Put(g, sb, 0, 0);
            U.Put(g, list.Scroll, 2, 0);
            View = g;
            sb.Tb.TextChanged += (s, e) => { if (!suppress) Refresh(true); };
            list.ContextRequested += (i, p) => ShowMenu(i, p);
        }

        public override void Shown()
        {
            Refresh(true);
            FocusMain();
        }

        public override void Hidden() { suppress = true; sb.Tb.Text = ""; suppress = false; }

        public override void FocusMain()
        {
            App.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(sb.FocusBox));
        }

        public override void Tick() { Refresh(false); }

        public override bool TypeInto(string text)
        {
            sb.FocusBox();
            sb.Tb.Text += text;
            sb.Tb.CaretIndex = sb.Tb.Text.Length;
            return true;
        }

        WinItem Sel { get { return list.Sel >= 0 && list.Sel < shown.Count ? shown[list.Sel] : null; } }

        public override void Key(KeyEventArgs e)
        {
            var k = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
            var mods = Keyboard.Modifiers;
            bool ctrl = (mods & ModifierKeys.Control) != 0, shift = (mods & ModifierKeys.Shift) != 0;
            var w = Sel;
            if (k == System.Windows.Input.Key.Down) { list.Move(1); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Up) { list.Move(-1); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Enter) { list.Run(); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Delete && sb.Tb.Text.Length == 0 && w != null) { WinList.Close(w.H); e.Handled = true; }
            else if (ctrl && k == System.Windows.Input.Key.W && w != null) { WinList.Close(w.H); e.Handled = true; }
            else if (ctrl && k == System.Windows.Input.Key.M && w != null) { WinList.Minimize(w.H); Refresh(true); e.Handled = true; }
            else if ((k == System.Windows.Input.Key.Apps || (k == System.Windows.Input.Key.F10 && shift)) && w != null)
            {
                ShowMenu(list.Sel, list.SelPoint()); e.Handled = true;
            }
            else if (!sb.Tb.IsKeyboardFocused && k != System.Windows.Input.Key.Tab) sb.FocusBox();
        }

        void ShowMenu(int i, Point at)
        {
            if (i < 0 || i >= shown.Count) return;
            var key = shown[i].Key;
            var ws = WinList.Get().Where(x => x.Key == key).ToList();
            // angeklicktes Fenster zuerst
            var h = shown[i].H;
            ws = ws.OrderBy(x => x.H == h ? 0 : 1).ToList();
            new MenuWindow(App, AppMenus.ForWindows(App, ws, () => Refresh(true)), at).Open();
        }

        void Refresh(bool force)
        {
            string q = sb.Tb.Text.Trim().ToLowerInvariant();
            var all = WinList.Get();
            var w = q.Length == 0 ? all : all.Where(x => (x.Title + " " + x.Name).ToLowerInvariant().Contains(q)).ToList();
            // nach App zusammenstellen (Reihenfolge der zuletzt benutzten App)
            bool grouped = App.Cfg.B("taskbar.group", true);
            var order = new List<string>();
            foreach (var x in w) if (!order.Contains(x.Key)) order.Add(x.Key);
            if (grouped) w = w.OrderBy(x => order.IndexOf(x.Key)).ToList();

            string s = q + "#" + string.Join("|", w.Select(x => x.H.ToString() + x.Title)) + "#" + string.Join("", w.Select(x => N.IsIconic(x.H) ? "1" : "0"));
            if (!force && s == sig) return;
            sig = s;
            IntPtr keepH = Sel != null ? Sel.H : IntPtr.Zero;
            shown = w;
            list.Clear();
            sb.SetHint(all.Count + Loc.Tr(" offen"));
            if (w.Count == 0)
            {
                list.Add(new Border { Margin = new Thickness(8, 10, 0, 0), Child = U.T(q.Length > 0 ? Loc.Tr("Keine Treffer.") : Loc.Tr("Keine Fenster geöffnet."), 13, Th.Muted) });
                return;
            }
            var counts = w.GroupBy(x => x.Key).ToDictionary(gr => gr.Key, gr => gr.Count());
            string lastKey = null;
            foreach (var win in w)
            {
                var x = win;
                int n = grouped ? counts[x.Key] : 1;
                bool firstOfGroup = x.Key != lastKey;
                lastKey = x.Key;
                if (n > 1 && firstOfGroup)
                {
                    var hg = U.G(null, new[] { U.Px(34), U.Px(12), U.Star(1) });
                    var ic0 = WinList.Icon(x);
                    U.Put(hg, ic0 != null ? (UIElement)new Image { Source = ic0, Width = 22, Height = 22 } : U.Letter(x.Name, 24), 0, 0);
                    U.Put(hg, U.T(x.Name + "  ·  " + n + Loc.Tr(" Fenster"), 12.5, Th.Muted, FontWeights.Bold), 0, 2);
                    hg.Margin = new Thickness(10, 8, 0, 4);
                    list.Add(hg);
                }
                bool child = n > 1;
                var g = U.G(null, new[] { U.Px(34), U.Px(12), U.Star(1), U.Auto, U.Px(4), U.Auto });
                if (child)
                {
                    U.Put(g, new Border { Width = 3, CornerRadius = new CornerRadius(1.5), Background = Th.Faint, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 6, 0, 6) }, 0, 0);
                }
                else
                {
                    var ic = WinList.Icon(x);
                    U.Put(g, ic != null ? (UIElement)new Image { Source = ic, Width = 26, Height = 26 } : U.Letter(x.Name, 28), 0, 0);
                }
                var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                sp.Children.Add(U.T(x.Title, 14, Th.Text, FontWeights.SemiBold));
                string sub = (child ? "" : x.Name) + (N.IsIconic(x.H) ? (child ? Loc.Tr("minimiert") : Loc.Tr("  ·  minimiert")) : "");
                if (sub.Length > 0) sp.Children.Add(U.T(sub, 11.5, Th.Muted));
                U.Put(g, sp, 0, 2);
                var min = U.IconBtn("", 30, 10, Brushes.Transparent, Th.Hover, Th.Muted);
                min.ToolTip = Loc.Tr("Minimieren (Strg+M)");
                min.Click += () => { WinList.Minimize(x.H); Refresh(true); };
                U.Put(g, min, 0, 3);
                var close = U.IconBtn("", 30, 10, Brushes.Transparent, Th.Hover, Th.Muted);
                close.ToolTip = Loc.Tr("Schließen (Entf)");
                close.Click += () => WinList.Close(x.H);
                U.Put(g, close, 0, 5);
                list.AddRow(SelList.Row(g, child ? 44 : 52), () => { WinList.Activate(x.H); App.ClosePanel(); }, Brushes.Transparent);
            }
            int idx = keepH == IntPtr.Zero || force && q.Length > 0 ? 0 : w.FindIndex(x => x.H == keepH);
            list.Select(idx < 0 ? 0 : idx);
        }
    }

    // -----------------------------------------------------------------
    //  Tab: Zwischenablage
    // -----------------------------------------------------------------
    public class ClipboardTab : TabBase
    {
        public override string Id { get { return "clipboard"; } }
        public override string Title { get { return Loc.Tr("Zwischenablage"); } }

        readonly SearchBox sb;
        readonly SelList list = new SelList();
        readonly Border detail;
        readonly StackPanel filterSp;
        string filter = "all";
        List<ClipItem> shown = new List<ClipItem>();
        bool visible;

        public ClipboardTab(NookApp app) : base(app)
        {
            filter = app.Cfg.S("clipboard.defaultFilter", "all");
            if (filter != "all" && filter != "text" && filter != "image" && filter != "link") filter = "all";
            var g = U.G(new[] { U.Px(52), U.Px(12), U.Star(1), U.Px(10), U.Auto }, null);

            var top = new Border();
            Th.StyleCard(top, -4);
            var tg = U.G(null, new[] { U.Star(1), U.Auto });
            sb = new SearchBox(Loc.Tr("Verlauf durchsuchen …"), null, 14.5, false);
            U.Put(tg, sb, 0, 0);
            filterSp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            U.Put(tg, filterSp, 0, 1);
            top.Child = tg;
            U.Put(g, top, 0, 0);

            var body = U.G(null, new[] { U.Star(0.47), U.Px(14), U.Star(0.53) });
            U.Put(body, list.Scroll, 0, 0);
            detail = new Border { Padding = new Thickness(20) };
            Th.StyleCard(detail, 0);
            U.Put(body, detail, 0, 2);
            U.Put(g, body, 2, 0);

            var foot = new StackPanel { Orientation = Orientation.Horizontal };
            foot.Children.Add(Kbd("Enter", Loc.Tr("Einfügen")));
            foot.Children.Add(Kbd(Loc.Tr("Strg C"), Loc.Tr("Kopieren")));
            foot.Children.Add(Kbd(Loc.Tr("Strg P"), Loc.Tr("Anheften")));
            foot.Children.Add(Kbd(Loc.Tr("Entf"), Loc.Tr("Entfernen")));
            U.Put(g, foot, 4, 0);
            View = g;

            BuildFilters();
            sb.Tb.TextChanged += (s, e) => Rebuild();
            list.SelChanged += i => ShowDetail(i >= 0 && i < shown.Count ? shown[i] : null);
            App.Clip.Changed += () => { if (visible) Rebuild(); };
        }

        static StackPanel Kbd(string key, string label)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
            sp.Children.Add(new Border
            {
                Background = Th.Surface2, CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3),
                Child = U.T(key, 11, Th.Muted, FontWeights.SemiBold), Margin = new Thickness(0, 0, 7, 0)
            });
            sp.Children.Add(U.T(label, 12, Th.Muted, FontWeights.SemiBold));
            return sp;
        }

        void BuildFilters()
        {
            filterSp.Children.Clear();
            var fs = new[] { new[] { "all", Loc.Tr("Alle") }, new[] { "text", "Text" }, new[] { "image", Loc.Tr("Bilder") }, new[] { "link", "Links" } };
            foreach (var f in fs)
            {
                string id = f[0];
                var chip = U.Chip(f[1], filter == id);
                chip.Margin = new Thickness(4, 0, 0, 0);
                chip.Click += () => { filter = id; BuildFilters(); Rebuild(); };
                filterSp.Children.Add(chip);
            }
        }

        public override void Shown()
        {
            visible = true;
            Rebuild();
            App.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(sb.FocusBox));
        }

        public override void Hidden() { visible = false; sb.Tb.Text = ""; }

        public override void FocusMain()
        {
            App.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(sb.FocusBox));
        }

        public override void Tick() { }

        public override void Key(KeyEventArgs e)
        {
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            var it = list.Sel >= 0 && list.Sel < shown.Count ? shown[list.Sel] : null;
            var k = e.Key;
            if (k == System.Windows.Input.Key.Down) { list.Move(1); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Up) { list.Move(-1); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Enter && it != null) { Paste(it); e.Handled = true; }
            else if (ctrl && k == System.Windows.Input.Key.C && it != null && sb.Tb.SelectionLength == 0) { App.Clip.Copy(it); App.Pill.Flash(Loc.Tr("Kopiert"), 1.2, false); e.Handled = true; }
            else if (ctrl && k == System.Windows.Input.Key.P && it != null) { App.Clip.TogglePin(it); e.Handled = true; }
            else if (k == System.Windows.Input.Key.Delete && it != null && sb.Tb.Text.Length == 0)
            {
                int keep = list.Sel; App.Clip.Remove(it); list.Select(keep); e.Handled = true;
            }
            else if (k == System.Windows.Input.Key.Tab && !ctrl)
            {
                var order = new[] { "all", "text", "image", "link" };
                filter = order[(Array.IndexOf(order, filter) + 1) % order.Length];
                BuildFilters(); Rebuild(); e.Handled = true;
            }
        }

        void Paste(ClipItem it)
        {
            App.Clip.Copy(it);
            App.PasteToPrevious();
        }

        void Rebuild()
        {
            int keep = list.Sel;
            list.Clear();
            shown = new List<ClipItem>();
            if (!App.Clip.Enabled)
            {
                list.Add(new Border { Margin = new Thickness(8, 10, 0, 0), Child = U.T(Loc.Tr("Verlauf ist in der Config ausgeschaltet."), 13, Th.Muted) });
                ShowDetail(null);
                return;
            }
            string q = sb.Tb.Text.Trim().ToLowerInvariant();
            var items = App.Clip.Items.Where(x =>
                (filter == "all" || x.Kind == filter) &&
                (q.Length == 0 || (x.Text != null && x.Text.ToLowerInvariant().Contains(q)) || (x.Img != null && "bild".Contains(q)))).ToList();

            var groups = new List<KeyValuePair<string, List<ClipItem>>>
            {
                new KeyValuePair<string, List<ClipItem>>(Loc.Tr("Angeheftet"), items.Where(x => x.Pinned).ToList()),
                new KeyValuePair<string, List<ClipItem>>(Loc.Tr("Heute"), items.Where(x => !x.Pinned && x.Time.Date == DateTime.Today).ToList()),
                new KeyValuePair<string, List<ClipItem>>(Loc.Tr("Gestern"), items.Where(x => !x.Pinned && x.Time.Date == DateTime.Today.AddDays(-1)).ToList()),
                new KeyValuePair<string, List<ClipItem>>(Loc.Tr("Früher"), items.Where(x => !x.Pinned && x.Time.Date < DateTime.Today.AddDays(-1)).ToList())
            };
            foreach (var grp in groups)
            {
                if (grp.Value.Count == 0) continue;
                list.Add(U.Section(grp.Key));
                foreach (var it in grp.Value) AddRow(it);
            }
            if (shown.Count == 0)
            {
                list.Add(new Border { Margin = new Thickness(8, 10, 0, 0), Child = U.T(q.Length > 0 ? Loc.Tr("Keine Treffer.") : Loc.Tr("Noch nichts kopiert."), 13, Th.Muted) });
                ShowDetail(null);
                return;
            }
            list.Select(keep < 0 ? 0 : Math.Min(keep, shown.Count - 1));
        }

        static string Glyph(ClipItem it)
        {
            if (it.Pinned) return "";
            var k = it.Kind;
            return k == "image" ? "" : k == "link" ? "" : "";
        }

        void AddRow(ClipItem it)
        {
            shown.Add(it);
            var g = U.G(null, new[] { U.Px(30), U.Px(10), U.Star(1), U.Auto });
            var ic = new Border { Width = 28, Height = 28, CornerRadius = new CornerRadius(8), Background = Th.Surface2, Child = U.I(Glyph(it), 12, it.Pinned ? Th.Accent : Th.Muted) };
            U.Put(g, ic, 0, 0);
            string text = it.Img != null
                ? Loc.Tr("Bild · ") + it.Img.PixelWidth + " × " + it.Img.PixelHeight
                : Regex.Replace(it.Text.Length > 300 ? it.Text.Substring(0, 300) : it.Text, @"\s+", " ").Trim();
            U.Put(g, U.T(text, 13.5, Th.Text, FontWeights.SemiBold), 0, 2);
            var ago = U.T(U.Ago(it.Time), 11.5, Th.Muted, FontWeights.SemiBold); ago.Margin = new Thickness(10, 0, 0, 0);
            U.Put(g, ago, 0, 3);
            var row = SelList.Row(g, 42);
            row.PreviewMouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Paste(it); e.Handled = true; } };
            list.AddRow(row, null, Brushes.Transparent);
        }

        void ShowDetail(ClipItem it)
        {
            if (it == null)
            {
                var e = U.T(Loc.Tr("Kopierte Texte, Links und Bilder erscheinen hier."), 13, Th.Muted);
                e.TextWrapping = TextWrapping.Wrap; e.HorizontalAlignment = HorizontalAlignment.Center; e.TextAlignment = TextAlignment.Center;
                detail.Child = e;
                return;
            }
            var g = U.G(new[] { U.Star(1), U.Px(14), U.Auto, U.Px(14), U.Auto }, null);
            if (it.Img != null)
            {
                U.Put(g, new Border { CornerRadius = new CornerRadius(10), Child = new Image { Source = it.Img, Stretch = Stretch.Uniform } }, 0, 0);
            }
            else
            {
                string t = it.Text.Length > 4000 ? it.Text.Substring(0, 4000) + " …" : it.Text;
                var tb = U.T(t, 14, Th.Text, FontWeights.SemiBold);
                tb.TextWrapping = TextWrapping.Wrap; tb.TextTrimming = TextTrimming.None; tb.TextAlignment = TextAlignment.Left;
                tb.VerticalAlignment = VerticalAlignment.Top;
                U.Put(g, U.Scroll(tb), 0, 0);
            }
            U.Put(g, new Border { Height = 1, Background = Th.Faint, VerticalAlignment = VerticalAlignment.Top }, 1, 0);
            var info = U.G(new[] { U.Auto, U.Auto, U.Auto }, new[] { U.Px(80), U.Star(1) });
            string type = it.Kind == "image" ? Loc.Tr("Bild") : it.Kind == "link" ? "Link" : "Text";
            string size = it.Img != null ? it.Img.PixelWidth + " × " + it.Img.PixelHeight + " px"
                : it.Text.Length + Loc.Tr(" Zeichen · ") + Regex.Matches(it.Text, @"\S+").Count + Loc.Tr(" Wörter · ") + (it.Text.Split('\n').Length) + Loc.Tr(" Zeilen");
            string when = (it.Time.Date == DateTime.Today ? Loc.Tr("Heute") : it.Time.Date == DateTime.Today.AddDays(-1) ? Loc.Tr("Gestern") : it.Time.ToString(Loc.Tr("d. MMM"), Th.Cul)) + ", " + it.Time.ToString("HH:mm", Th.Cul);
            InfoRow(info, 0, Loc.Tr("Typ"), type); InfoRow(info, 1, Loc.Tr("Größe"), size); InfoRow(info, 2, Loc.Tr("Kopiert"), when);
            U.Put(g, info, 2, 0);
            var bs = new StackPanel { Orientation = Orientation.Horizontal };
            bs.Children.Add(ActBtn(Loc.Tr("Einfügen"), true, () => Paste(it)));
            bs.Children.Add(ActBtn(Loc.Tr("Kopieren"), false, () => { App.Clip.Copy(it); App.Pill.Flash(Loc.Tr("Kopiert"), 1.2, false); }));
            bs.Children.Add(ActBtn(it.Pinned ? Loc.Tr("Lösen") : Loc.Tr("Anheften"), false, () => App.Clip.TogglePin(it)));
            bs.Children.Add(ActBtn(Loc.Tr("Löschen"), false, () => App.Clip.Remove(it)));
            U.Put(g, bs, 4, 0);
            detail.Child = g;
        }

        static void InfoRow(Grid g, int r, string k, string v)
        {
            var a = U.T(k, 12.5, Th.Muted, FontWeights.SemiBold); a.Margin = new Thickness(0, 2, 0, 2);
            U.Put(g, a, r, 0);
            U.Put(g, U.T(v, 12.5, Th.Text, FontWeights.SemiBold), r, 1);
        }

        static Btn ActBtn(string text, bool primary, Action act)
        {
            var b = new Btn(U.T(text, 12.5, primary ? Th.AccentText : Th.Text, FontWeights.SemiBold),
                primary ? Th.Accent : Th.Surface2, primary ? Th.AccentHover : Th.Hover, 10, new Thickness(12, 6, 12, 7));
            b.Margin = new Thickness(0, 0, 6, 0);
            b.Click += act;
            return b;
        }
    }

    // -----------------------------------------------------------------
    //  Tab: Power
    // -----------------------------------------------------------------
    public class PowerTab : TabBase
    {
        public override string Id { get { return "power"; } }
        public override string Title { get { return "Power"; } }
        public override bool AutoHeight { get { return true; } }

        readonly TextBlock uptime;
        string armed;
        DateTime armedAt;
        readonly Dictionary<string, TextBlock> labels = new Dictionary<string, TextBlock>();
        readonly Dictionary<string, string> names = new Dictionary<string, string>();

        public PowerTab(NookApp app) : base(app)
        {
            var g = U.G(new[] { U.Auto, U.Px(14), U.Auto }, null);
            var defs = new Dictionary<string, Func<UIElement>>
            {
                { "lock", () => MakeTile("lock", "L", "\uE72E", Loc.Tr("Sperren"), false, () => N.LockWorkStation()) },
                { "sleep", () => MakeTile("sleep", "E", "\uE708", Loc.Tr("Energie sparen"), false, () => N.SetSuspendState(false, false, false)) },
                { "logoff", () => MakeTile("logoff", "A", "\uF3B1", Loc.Tr("Abmelden"), true, () => Run("shutdown", "/l")) },
                { "restart", () => MakeTile("restart", "N", "\uE777", Loc.Tr("Neu starten"), true, () => Run("shutdown", "/r /t 0")) },
                { "shutdown", () => MakeTile("shutdown", "H", "\uE7E8", Loc.Tr("Herunterfahren"), true, () => Run("shutdown", "/s /t 0")) }
            };
            var ids = app.Cfg.L("power.buttons").Select(x => Convert.ToString(x)).Where(defs.ContainsKey).Distinct().ToList();
            if (ids.Count == 0) ids = new List<string> { "lock" };
            var row = new UniformGrid { Rows = 1, Columns = ids.Count };
            foreach (var id in ids) row.Children.Add(defs[id]());
            U.Put(g, row, 0, 0);
            uptime = U.T("", 12.5, Th.Muted, FontWeights.SemiBold); uptime.HorizontalAlignment = HorizontalAlignment.Center;
            U.Put(g, uptime, 2, 0);
            View = g;
        }

        readonly Dictionary<string, Action> keys = new Dictionary<string, Action>();

        UIElement MakeTile(string id, string letter, string glyph, string label, bool confirm, Action act)
        {
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(U.I(glyph, 24, Th.Text));
            var l = U.T(label, 13.5, Th.Text, FontWeights.SemiBold); l.HorizontalAlignment = HorizontalAlignment.Center; l.Margin = new Thickness(0, 12, 0, 0);
            sp.Children.Add(l);
            labels[id] = l; names[id] = label;
            var b = new Btn(sp, Th.CardBg, Th.Hover, Th.CardR + 2, new Thickness(8)) { Height = 128, Margin = new Thickness(5, 0, 5, 0) };
            b.BorderBrush = Th.CardBorder; b.BorderThickness = new Thickness(Th.CardBorderW);
            Action click = () =>
            {
                if (confirm && App.Cfg.B("power.confirm", true) && (armed != id || (DateTime.Now - armedAt).TotalSeconds > 4))
                {
                    Disarm();
                    armed = id; armedAt = DateTime.Now;
                    l.Text = Loc.Tr("Nochmal (Alt+") + letter + ")"; l.Foreground = Th.Accent;
                    return;
                }
                Disarm();
                App.ClosePanel();
                if (id == "restart" || id == "shutdown" || id == "logoff") App.Cleanup();
                act();
            };
            b.Click += click;
            keys[letter] = click;
            var hint = new Border
            {
                Background = Th.Surface2, CornerRadius = new CornerRadius(6), Padding = new Thickness(6, 1, 6, 2), IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 10, 15, 0),
                Child = U.T("Alt+" + letter, 10.5, Th.Muted, FontWeights.Bold)
            };
            var host = new Grid();
            host.Children.Add(b); host.Children.Add(hint);
            return host;
        }

        void Disarm()
        {
            armed = null;
            foreach (var kv in labels) { kv.Value.Text = names[kv.Key]; kv.Value.Foreground = Th.Text; }
        }

        static void Run(string exe, string args)
        {
            try { Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true }); }
            catch (Exception ex) { NookApp.Log(ex); }
        }

        public override void Shown() { Disarm(); Tick(); }
        public override void Hidden() { Disarm(); }

        public override void Key(KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.Alt) return;
            string k = (e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key).ToString();
            Action a;
            if (k.Length == 1 && keys.TryGetValue(k, out a)) { a(); e.Handled = true; }
        }

        public override void Tick()
        {
            var t = TimeSpan.FromMilliseconds(N.GetTickCount64());
            string s = t.TotalDays >= 1 ? (int)t.TotalDays + Loc.Tr(" T ") + t.Hours + Loc.Tr(" Std") : t.Hours + Loc.Tr(" Std ") + t.Minutes + Loc.Tr(" Min");
            uptime.Text = Loc.Tr("Eingeschaltet seit ") + s;
            if (armed != null && (DateTime.Now - armedAt).TotalSeconds > 4) Disarm();
        }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Menüeintrag
    // -----------------------------------------------------------------
    public class MI
    {
        public string Text, Glyph, Key;
        public Action Do;
        public bool Header, Danger;

        public static MI Item(string text, string glyph, Action a) { return new MI { Text = text, Glyph = glyph, Do = a }; }
        public static MI Item(string text, string glyph, string key, Action a) { return new MI { Text = text, Glyph = glyph, Key = key, Do = a }; }
        public static MI Sep() { return new MI(); }
        public static MI Head(string text) { return new MI { Text = text, Header = true }; }
        public bool IsSep { get { return Text == null; } }
    }

    // -----------------------------------------------------------------
    //  Zusätzliche Windows-Funktionen
    // -----------------------------------------------------------------
    public static class NX
    {
        [DllImport("shlwapi.dll")] public static extern IntPtr SHCreateMemStream(byte[] p, uint cb);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int SHLoadIndirectString(string src, StringBuilder outBuf, uint cch, IntPtr reserved);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        public static extern int SHGetPropertyStoreFromParsingName(string path, IntPtr pbc, int flags, ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out object ppv);
        [DllImport("shell32.dll")] public static extern int SHGetKnownFolderPath(ref Guid id, uint flags, IntPtr token, out IntPtr path);
        [DllImport("ole32.dll")] public static extern void CoTaskMemFree(IntPtr p);
        [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(POINT p);
        [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h, uint flags);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);

        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int X, Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData, flags, time; public IntPtr extra; }

        public static string Indirect(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '@') return s;
            var sb = new StringBuilder(1024);
            return SHLoadIndirectString(s, sb, 1024, IntPtr.Zero) == 0 ? sb.ToString() : null;
        }

        public static string PropString(IPropertyStore ps, Guid fmt, int pid)
        {
            if (ps == null) return null;
            var k = new PROPERTYKEY { fmtid = fmt, pid = pid };
            PROPVARIANT pv;
            if (ps.GetValue(ref k, out pv) != 0) return null;
            string r = pv.vt == 31 ? Marshal.PtrToStringUni(pv.p) : null;
            N.PropVariantClear(ref pv);
            return r;
        }

        public static bool PropBool(IPropertyStore ps, Guid fmt, int pid)
        {
            if (ps == null) return false;
            var k = new PROPERTYKEY { fmtid = fmt, pid = pid };
            PROPVARIANT pv;
            if (ps.GetValue(ref k, out pv) != 0) return false;
            bool r = pv.vt == 11 && pv.boolVal != 0;
            N.PropVariantClear(ref pv);
            return r;
        }

        public static IPropertyStore StoreFor(string parsingName)
        {
            object o;
            Guid iid = typeof(IPropertyStore).GUID;
            if (SHGetPropertyStoreFromParsingName(parsingName, IntPtr.Zero, 0, ref iid, out o) != 0) return null;
            return o as IPropertyStore;
        }

        // "{GUID}\Pfad\app.exe" -> echter Pfad
        public static string ResolveKnownFolder(string p)
        {
            if (string.IsNullOrEmpty(p) || p[0] != '{') return p;
            int i = p.IndexOf('}');
            if (i < 0) return p;
            Guid g;
            if (!Guid.TryParse(p.Substring(0, i + 1), out g)) return p;
            IntPtr ptr;
            if (SHGetKnownFolderPath(ref g, 0, IntPtr.Zero, out ptr) != 0) return p;
            string root = Marshal.PtrToStringUni(ptr);
            CoTaskMemFree(ptr);
            return root + p.Substring(i + 1);
        }

        public static uint PidAt(int x, int y)
        {
            var h = WindowFromPoint(new POINT { X = x, Y = y });
            if (h == IntPtr.Zero) return 0;
            var root = GetAncestor(h, 2);
            uint pid; N.GetWindowThreadProcessId(root == IntPtr.Zero ? h : root, out pid);
            return pid;
        }
    }

    // -----------------------------------------------------------------
    //  Klick außerhalb von Nook erkennen (nur solange das Panel offen ist)
    // -----------------------------------------------------------------
    public class OutsideClick
    {
        N.HookProc proc;
        IntPtr hh;
        readonly Action onOutside;
        readonly Func<IntPtr, bool> inside;
        static readonly uint Me = (uint)Process.GetCurrentProcess().Id;

        // Standard: alles außerhalb von Nook zählt als "außen"
        public OutsideClick(Action a) { onOutside = a; }

        // Eigene Prüfung: inside(rootFenster) = true -> kein Schließen
        public OutsideClick(Action a, Func<IntPtr, bool> isInside) { onOutside = a; inside = isInside; }

        public void Start()
        {
            if (hh != IntPtr.Zero) return;
            proc = Cb;
            using (var p = Process.GetCurrentProcess())
            using (var m = p.MainModule)
                hh = N.SetWindowsHookEx(14, proc, N.GetModuleHandle(m.ModuleName), 0);
        }

        public void Stop()
        {
            if (hh != IntPtr.Zero) { N.UnhookWindowsHookEx(hh); hh = IntPtr.Zero; }
        }

        IntPtr Cb(int code, IntPtr w, IntPtr l)
        {
            if (code >= 0)
            {
                int msg = w.ToInt32();
                if (msg == 0x201 || msg == 0x204 || msg == 0x207 || msg == 0x20B)
                {
                    var d = (NX.MSLLHOOKSTRUCT)Marshal.PtrToStructure(l, typeof(NX.MSLLHOOKSTRUCT));
                    bool outside;
                    if (inside != null)
                    {
                        var hw = NX.WindowFromPoint(d.pt);
                        var root = hw == IntPtr.Zero ? IntPtr.Zero : NX.GetAncestor(hw, 2);
                        outside = !inside(root == IntPtr.Zero ? hw : root);
                    }
                    else outside = NX.PidAt(d.pt.X, d.pt.Y) != Me;
                    if (outside) Application.Current.Dispatcher.BeginInvoke(onOutside);
                }
            }
            return N.CallNextHookEx(hh, code, w, l);
        }
    }

    // -----------------------------------------------------------------
    //  Jumplists ("Neues Fenster", "Neuer Chat" …) aus den Windows-Daten lesen
    // -----------------------------------------------------------------
    [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
    class ShellLinkCom { }

    [ComImport, Guid("000214F9-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellLinkW
    {
        [PreserveSig] int GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder file, int cch, IntPtr fd, uint flags);
        [PreserveSig] int GetIDList(out IntPtr pidl);
        [PreserveSig] int SetIDList(IntPtr pidl);
        [PreserveSig] int GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder name, int cch);
        [PreserveSig] int SetDescription([MarshalAs(UnmanagedType.LPWStr)] string name);
        [PreserveSig] int GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder dir, int cch);
        [PreserveSig] int SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string dir);
        [PreserveSig] int GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder args, int cch);
        [PreserveSig] int SetArguments([MarshalAs(UnmanagedType.LPWStr)] string args);
        [PreserveSig] int GetHotkey(out short hk);
        [PreserveSig] int SetHotkey(short hk);
        [PreserveSig] int GetShowCmd(out int cmd);
        [PreserveSig] int SetShowCmd(int cmd);
        [PreserveSig] int GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder path, int cch, out int idx);
        [PreserveSig] int SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string path, int idx);
        [PreserveSig] int SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string path, int reserved);
        [PreserveSig] int Resolve(IntPtr hwnd, int flags);
        [PreserveSig] int SetPath([MarshalAs(UnmanagedType.LPWStr)] string file);
    }

    [ComImport, Guid("00000109-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPersistStream
    {
        [PreserveSig] int GetClassID(out Guid clsid);
        [PreserveSig] int IsDirty();
        [PreserveSig] int Load(IntPtr stm);
        [PreserveSig] int Save(IntPtr stm, bool clearDirty);
        [PreserveSig] int GetSizeMax(out long size);
    }

    public class JumpTask
    {
        public string Title, Path, Args, Dir;
    }

    public static class JumpLists
    {
        static readonly Guid TitleFmt = new Guid("F29F85E0-4FF9-1068-AB91-08002B27B3D9");
        static readonly Guid AumFmt = new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");
        static readonly byte[] LnkHead = { 0x4C, 0, 0, 0, 0x01, 0x14, 0x02, 0, 0, 0, 0, 0, 0xC0, 0, 0, 0, 0, 0, 0, 0x46 };
        static readonly Dictionary<string, KeyValuePair<DateTime, List<JumpTask>>> cache = new Dictionary<string, KeyValuePair<DateTime, List<JumpTask>>>(StringComparer.OrdinalIgnoreCase);

        static string Dir { get { return System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Recent\CustomDestinations"); } }

        // Aufgaben für ein Programm (über den Pfad der .exe zugeordnet)
        public static List<JumpTask> For(string exePath)
        {
            var res = new List<JumpTask>();
            if (string.IsNullOrEmpty(exePath)) return res;
            try
            {
                if (!Directory.Exists(Dir)) return res;
                string exeName = System.IO.Path.GetFileName(exePath);
                string exeDir = System.IO.Path.GetDirectoryName(exePath) ?? "";
                var files = new DirectoryInfo(Dir).GetFiles("*.customDestinations-ms").OrderByDescending(f => f.LastWriteTimeUtc);
                foreach (var f in files)
                {
                    var tasks = Read(f);
                    if (tasks.Count == 0) continue;
                    if (tasks.Any(t => Matches(t.Path, exeName, exeDir))) return tasks;
                }
            }
            catch (Exception ex) { NookApp.Log(ex); }
            return res;
        }

        static bool Matches(string target, string exeName, string exeDir)
        {
            if (string.IsNullOrEmpty(target)) return false;
            if (System.IO.Path.GetFileName(target).Equals(exeName, StringComparison.OrdinalIgnoreCase)) return true;
            string td = System.IO.Path.GetDirectoryName(target) ?? "";
            if (td.Length <= 3) return false;
            if (td.StartsWith(Environment.GetFolderPath(Environment.SpecialFolder.Windows), StringComparison.OrdinalIgnoreCase)) return false;
            if (td.Equals(exeDir, StringComparison.OrdinalIgnoreCase)) return true;
            // Squirrel-Apps (z. B. Claude, Discord): Update.exe liegt eine Ebene über der App
            return exeDir.StartsWith(td + "\\", StringComparison.OrdinalIgnoreCase) && System.IO.Path.GetFileName(target).Equals("Update.exe", StringComparison.OrdinalIgnoreCase);
        }

        static List<JumpTask> Read(FileInfo f)
        {
            KeyValuePair<DateTime, List<JumpTask>> c;
            if (cache.TryGetValue(f.FullName, out c) && c.Key == f.LastWriteTimeUtc) return c.Value;
            var list = new List<JumpTask>();
            byte[] data;
            try { data = File.ReadAllBytes(f.FullName); } catch { return list; }
            for (int i = 0; i + LnkHead.Length < data.Length; i++)
            {
                if (data[i] != 0x4C) continue;
                bool hit = true;
                for (int j = 1; j < LnkHead.Length; j++) if (data[i + j] != LnkHead[j]) { hit = false; break; }
                if (!hit) continue;
                var t = ParseLink(data, i);
                if (t != null && !list.Any(x => x.Title == t.Title)) list.Add(t);
                if (list.Count >= 16) break;
            }
            cache[f.FullName] = new KeyValuePair<DateTime, List<JumpTask>>(f.LastWriteTimeUtc, list);
            return list;
        }

        static JumpTask ParseLink(byte[] data, int offset)
        {
            object link = null;
            IntPtr stm = IntPtr.Zero;
            try
            {
                var part = new byte[data.Length - offset];
                Buffer.BlockCopy(data, offset, part, 0, part.Length);
                stm = NX.SHCreateMemStream(part, (uint)part.Length);
                if (stm == IntPtr.Zero) return null;
                link = new ShellLinkCom();
                if (((IPersistStream)link).Load(stm) != 0) return null;
                var sl = (IShellLinkW)link;
                var ps = link as IPropertyStore;
                if (NX.PropBool(ps, AumFmt, 6)) return null;                     // Trennlinie
                var sb = new StringBuilder(1024);
                sl.GetPath(sb, 1024, IntPtr.Zero, 4);
                string path = Environment.ExpandEnvironmentVariables(sb.ToString());
                if (string.IsNullOrEmpty(path)) return null;
                var ab = new StringBuilder(4096);
                sl.GetArguments(ab, ab.Capacity);
                string args = ab.ToString();
                sb.Clear(); sl.GetWorkingDirectory(sb, sb.Capacity);
                string dir = Environment.ExpandEnvironmentVariables(sb.ToString());
                string title = NX.Indirect(NX.PropString(ps, TitleFmt, 2));
                if (string.IsNullOrWhiteSpace(title)) { sb.Clear(); sl.GetDescription(sb, sb.Capacity); title = NX.Indirect(sb.ToString()); }
                if (string.IsNullOrWhiteSpace(title)) return null;
                return new JumpTask { Title = title.Trim(), Path = path, Args = args, Dir = dir };
            }
            catch { return null; }
            finally
            {
                if (stm != IntPtr.Zero) Marshal.Release(stm);
                if (link != null) Marshal.ReleaseComObject(link);
            }
        }

        public static void Run(JumpTask t)
        {
            try
            {
                var psi = new ProcessStartInfo(t.Path, t.Args ?? "") { UseShellExecute = true };
                if (!string.IsNullOrEmpty(t.Dir) && Directory.Exists(t.Dir)) psi.WorkingDirectory = t.Dir;
                Process.Start(psi);
            }
            catch (Exception ex) { NookApp.Log(ex); }
        }
    }

    // -----------------------------------------------------------------
    //  Menüs für Apps und Fenster (wie in der Windows-Taskleiste)
    // -----------------------------------------------------------------
    public static class AppMenus
    {
        static readonly Guid LinkFmt = new Guid("B9B4B3FC-2B51-4A42-B5D8-324146AFCF25");

        // Windows-11-Einstellung "Task beenden" in der Taskleiste
        public static bool EndTaskEnabled()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"))
                {
                    var v = k == null ? null : k.GetValue("TaskbarEndTask");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        public static string TargetOf(AppEntry a)
        {
            if (a.Target != null) return a.Target.Length == 0 ? null : a.Target;
            string t = null;
            try
            {
                var ps = NX.StoreFor(a.Shell ? "shell:AppsFolder\\" + a.Parse : a.Parse);
                t = NX.PropString(ps, LinkFmt, 2);
                if (ps != null) Marshal.ReleaseComObject(ps);
            }
            catch { }
            if (string.IsNullOrEmpty(t) && a.Shell && !a.Parse.Contains("!")) t = NX.ResolveKnownFolder(a.Parse);
            if (!string.IsNullOrEmpty(t)) t = Environment.ExpandEnvironmentVariables(t);
            if (string.IsNullOrEmpty(t) || !File.Exists(t)) t = "";
            a.Target = t;
            return t.Length == 0 ? null : t;
        }

        static void Start(ProcessStartInfo psi)
        {
            try { Process.Start(psi); }
            catch (System.ComponentModel.Win32Exception ex) { if (ex.NativeErrorCode != 1223) NookApp.Log(ex); }   // 1223 = UAC abgebrochen
            catch (Exception ex) { NookApp.Log(ex); }
        }

        public static void RunAsAdmin(string path)
        {
            Start(new ProcessStartInfo(path) { UseShellExecute = true, Verb = "runas", WorkingDirectory = System.IO.Path.GetDirectoryName(path) });
        }

        public static void ShowInFolder(string path)
        {
            Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
        }

        public static void Uninstall() { AppIndex.Open("ms-settings:appsfeatures"); }

        static void AddJump(List<MI> items, string exe)
        {
            var tasks = JumpLists.For(exe);
            if (tasks.Count == 0) return;
            items.Add(MI.Head(Loc.Tr("Aufgaben")));
            foreach (var t in tasks.Take(10))
            {
                var tt = t;
                items.Add(MI.Item(tt.Title, "", () => JumpLists.Run(tt)));
            }
            items.Add(MI.Sep());
        }

        // Menü für einen Eintrag in der App-Suche
        public static List<MI> ForApp(NookApp app, AppEntry a, Action launch, Action toggleStar, bool starred)
        {
            string exe = TargetOf(a);
            var items = new List<MI>();
            if (exe != null) AddJump(items, exe);
            items.Add(MI.Item(Loc.Tr("Öffnen"), "", "Enter", launch));
            if (exe != null)
            {
                items.Add(MI.Item(Loc.Tr("Als Administrator ausführen"), "", Loc.Tr("Umschalt+Enter"), () => { app.ClosePanel(); RunAsAdmin(exe); }));
                items.Add(MI.Item(Loc.Tr("Dateispeicherort öffnen"), "", Loc.Tr("Strg+O"), () => { app.ClosePanel(); ShowInFolder(exe); }));
            }
            items.Add(MI.Item(starred ? Loc.Tr("Von Favoriten lösen") : Loc.Tr("Zu Favoriten"), starred ? "" : "", Loc.Tr("Strg+S"), toggleStar));
            items.Add(MI.Sep());
            items.Add(MI.Item(Loc.Tr("Deinstallieren"), "", () => { app.ClosePanel(); Uninstall(); }));
            return items;
        }

        // Menü für eine laufende App (Gruppe von Fenstern)
        public static List<MI> ForWindows(NookApp app, List<WinItem> ws, Action after)
        {
            var items = new List<MI>();
            if (ws.Count == 0) return items;
            var first = ws[0];
            if (!string.IsNullOrEmpty(first.Path))
            {
                AddJump(items, first.Path);
                string p = first.Path;
                items.Add(MI.Item(first.Name + Loc.Tr(" starten"), "", () => { app.ClosePanel(); Start(new ProcessStartInfo(p) { UseShellExecute = true }); }));
            }
            if (ws.Count > 1)
            {
                items.Add(MI.Head(Loc.Tr("Fenster")));
                foreach (var w in ws.Take(12))
                {
                    var h = w.H;
                    items.Add(MI.Item(w.Title, "", () => { WinList.Activate(h); app.ClosePanel(); if (after != null) after(); }));
                }
                items.Add(MI.Sep());
            }
            else
            {
                var h = first.H;
                items.Add(MI.Item(Loc.Tr("Minimieren"), "", () => { WinList.Minimize(h); if (after != null) after(); }));
            }
            if (!string.IsNullOrEmpty(first.Path))
            {
                string p = first.Path;
                items.Add(MI.Item(Loc.Tr("Dateispeicherort öffnen"), "", () => { app.ClosePanel(); ShowInFolder(p); }));
            }
            items.Add(MI.Sep());
            if (ws.Count > 1)
                items.Add(MI.Item(Loc.Tr("Alle Fenster schließen"), "", () => { foreach (var w in ws) WinList.Close(w.H); if (after != null) after(); }));
            else
            {
                var h = first.H;
                items.Add(MI.Item(Loc.Tr("Fenster schließen"), "", Loc.Tr("Entf"), () => { WinList.Close(h); if (after != null) after(); }));
            }
            bool isExplorer = !string.IsNullOrEmpty(first.Path) && System.IO.Path.GetFileName(first.Path).Equals("explorer.exe", StringComparison.OrdinalIgnoreCase);
            if (EndTaskEnabled() && !isExplorer)
            {
                var pids = ws.Select(w => w.Pid).Distinct().ToList();
                var end = MI.Item(Loc.Tr("Task beenden"), "", () =>
                {
                    foreach (var pid in pids)
                    {
                        try { using (var pr = Process.GetProcessById((int)pid)) pr.Kill(); } catch (Exception ex) { NookApp.Log(ex); }
                    }
                    if (after != null) after();
                });
                end.Danger = true;
                items.Add(end);
            }
            return items;
        }
    }

    // -----------------------------------------------------------------
    //  Fensterauswahl beim Überfahren eines App-Icons in der Pille
    // -----------------------------------------------------------------
    public class PeekWindow : Window
    {
        readonly NookApp app;
        public string Key;
        readonly StackPanel sp = new StackPanel();
        public IntPtr Handle;

        public PeekWindow(NookApp a)
        {
            app = a;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
            SizeToContent = SizeToContent.WidthAndHeight; Title = "Nook Fenster";
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Content = new Border
            {
                Background = Th.Bg, CornerRadius = new CornerRadius(14), Padding = new Thickness(6), Child = sp, Margin = new Thickness(16),
                LayoutTransform = new ScaleTransform(Th.K, Th.K),
                Effect = new DropShadowEffect { BlurRadius = 20, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black }
            };
            SourceInitialized += (s, e) =>
            {
                Handle = new WindowInteropHelper(this).Handle;
                N.AddExStyle(Handle, 0x80 | 0x08000000);
            };
            Left = -10000; Top = -10000;
        }

        public void ShowFor(string key, List<WinItem> ws, Rect anchor)
        {
            Key = key;
            sp.Children.Clear();
            var head = U.T(ws[0].Name + "  ·  " + ws.Count + Loc.Tr(" Fenster"), 11.5, Th.Muted, FontWeights.SemiBold);
            head.Margin = new Thickness(10, 4, 10, 6);
            sp.Children.Add(head);
            IntPtr fg = N.GetForegroundWindow();
            foreach (var w in ws.Take(14))
            {
                var x = w;
                var g = U.G(null, new[] { U.Px(22), U.Px(10), U.Star(1), U.Px(28) });
                var ic = WinList.Icon(x);
                U.Put(g, ic != null ? (UIElement)new Image { Source = ic, Width = 18, Height = 18 } : U.Letter(x.Name, 18), 0, 0);
                var t = U.T(x.Title, 13, x.H == fg ? Th.Accent : Th.Text, FontWeights.SemiBold);
                t.MaxWidth = 340;
                U.Put(g, t, 0, 2);
                var close = U.IconBtn("", 24, 9, Brushes.Transparent, Th.Surface2, Th.Muted);
                close.Click += () => { WinList.Close(x.H); app.Pill.RefreshWindowsSoon(); Dispatcher.BeginInvoke(new Action(Refill), DispatcherPriority.Background); };
                U.Put(g, close, 0, 3);
                var row = new Btn(g, Brushes.Transparent, Th.Hover, 9, new Thickness(8, 6, 4, 6)) { MinWidth = 240 };
                row.Click += () => { WinList.Activate(x.H); HideNow(); app.Pill.RefreshWindowsSoon(); };
                sp.Children.Add(row);
            }
            if (!IsVisible) Show();
            UpdateLayout();
            var mon = app.MonitorDip();
            double x0 = anchor.X + anchor.Width / 2 - ActualWidth / 2;
            x0 = Math.Max(mon.Left + 4, Math.Min(x0, mon.Right - ActualWidth - 4));
            bool below = anchor.Y < mon.Top + mon.Height / 2;
            Left = x0;
            Top = below ? anchor.Bottom - 10 * Th.K : anchor.Top - ActualHeight + 10 * Th.K;
            if (Handle != IntPtr.Zero) N.SetWindowPos(Handle, N.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }

        void Refill()
        {
            var ws = WinList.Get().Where(w => w.Key == Key).ToList();
            if (ws.Count < 2) { HideNow(); return; }
            ShowFor(Key, ws, new Rect(Left + ActualWidth / 2, Top, 0, 0));
        }

        public void HideNow() { Key = null; if (IsVisible) Hide(); }
    }

    // -----------------------------------------------------------------
    //  Übersicht der Tastenkürzel
    // -----------------------------------------------------------------
    public static class Shortcuts
    {
        public static readonly string[][] All =
        {
            new[] { "#Überall", "" },
            new[] { "Win", "Nook öffnen / schließen" },
            new[] { "Tippen", "Springt direkt zur Suche" },
            new[] { "Esc", "Schließen" },
            new[] { "F1", "Diese Übersicht" },
            new[] { "← →", "Tab wechseln" },
            new[] { "Strg+1 … 6", "Direkt zu Tab" },
            new[] { "Strg+,", "Einstellungen" },
            new[] { "Menütaste", "Menü zur Auswahl / Nook-Menü" },
            new[] { "#Apps", "" },
            new[] { "Tippen", "Suchen oder rechnen" },
            new[] { "↑ ↓  Enter", "Auswählen, öffnen" },
            new[] { "Umschalt+Enter", "Als Administrator" },
            new[] { "Strg+O", "Speicherort öffnen" },
            new[] { "Strg+S", "Favorit an/aus" },
            new[] { "#Taskbar", "" },
            new[] { "Tippen", "Fenster filtern" },
            new[] { "Enter", "Fenster öffnen" },
            new[] { "Strg+M", "Minimieren" },
            new[] { "Entf", "Fenster schließen" },
            new[] { "#Steuerung", "" },
            new[] { "↑ ↓", "Lautstärke" },
            new[] { "Leertaste", "Play / Pause" },
            new[] { "Alt+N  Alt+V", "Nächster / voriger Titel" },
            new[] { "Umschalt+← →", "10 s spulen" },
            new[] { "Alt+M", "Stumm" },
            new[] { "Alt+1 … 6", "Kacheln" },
            new[] { "Audio-Fenster: ↑ ↓ ← →", "Gerät wählen, App-Lautstärke" },
            new[] { "#Heute", "" },
            new[] { "Alt+S  Alt+R", "Timer Start/Pause, Zurücksetzen" },
            new[] { "+  −", "Timer ±1 Minute" },
            new[] { "Alt+N", "Neues To-do" },
            new[] { "↑ ↓  Leertaste", "To-do wählen, abhaken" },
            new[] { "Entf", "To-do löschen" },
            new[] { "#Zwischenablage", "" },
            new[] { "Enter", "Einfügen" },
            new[] { "Strg+C  Strg+P", "Kopieren, anheften" },
            new[] { "Tab", "Filter wechseln" },
            new[] { "#Power", "" },
            new[] { "Alt+L  E  A", "Sperren, Energie sparen, Abmelden" },
            new[] { "Alt+N  H", "Neu starten, Herunterfahren (2×)" },
        };

        public static FrameworkElement Build(Action close)
        {
            var outer = U.G(new[] { U.Auto, U.Px(12), U.Star(1), U.Px(10), U.Auto }, null);
            var head = U.G(null, new[] { U.Auto, U.Px(12), U.Star(1), U.Auto });
            var logo = NookApp.Logo(30);
            if (logo != null) U.Put(head, new Image { Source = logo, Width = 30, Height = 30 }, 0, 0);
            U.Put(head, U.T(Loc.Tr("Tastenkürzel"), 17, Th.Text, FontWeights.Bold), 0, 2);
            var x = U.IconBtn("", 30, 10, Th.Surface2, Th.Hover, Th.Muted);
            x.Click += close;
            U.Put(head, x, 0, 3);
            U.Put(outer, head, 0, 0);

            var cols = U.G(null, new[] { U.Star(1), U.Px(24), U.Star(1) });
            var left = new StackPanel(); var right = new StackPanel();
            U.Put(cols, left, 0, 0); U.Put(cols, right, 0, 2);
            int half = All.Length / 2, idx = 0;
            StackPanel target = left;
            foreach (var r in All)
            {
                if (idx >= half && r[0].StartsWith("#") && target == left) target = right;
                idx++;
                if (r[0].StartsWith("#"))
                {
                    var h = U.T(Loc.Tr(r[0].Substring(1)).ToUpper(Th.Cul), 11, Th.Accent, FontWeights.Bold);
                    h.Margin = new Thickness(0, target.Children.Count == 0 ? 0 : 12, 0, 4);
                    target.Children.Add(h);
                    continue;
                }
                var g = U.G(null, new[] { U.Px(138), U.Star(1) });
                var k = new Border
                {
                    Background = Th.Surface2, CornerRadius = new CornerRadius(6), Padding = new Thickness(7, 2, 7, 3),
                    HorizontalAlignment = HorizontalAlignment.Left, Child = U.T(r[0], 11.5, Th.Text, FontWeights.SemiBold)
                };
                U.Put(g, k, 0, 0);
                U.Put(g, U.T(r[1], 12.5, Th.Muted, FontWeights.SemiBold), 0, 1);
                g.Margin = new Thickness(0, 2, 0, 2);
                target.Children.Add(g);
            }
            U.Put(outer, U.Scroll(cols), 2, 0);
            var foot = U.T(Loc.Tr("F1 oder Esc schließt diese Übersicht  ·  Das ?-Symbol oben öffnet sie auch"), 11.5, Th.Muted, FontWeights.SemiBold);
            foot.HorizontalAlignment = HorizontalAlignment.Center;
            U.Put(outer, foot, 4, 0);
            return new Border { Background = Th.Bg, Padding = new Thickness(22, 18, 22, 14), Child = outer };
        }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Lippe: dünner Strich am Bildschirmrand, zeigt wo die Pille wartet
    //  (klick-durchlässig; das Aufklappen erkennt die Pille über die Mausposition)
    // -----------------------------------------------------------------
    public class LipWindow : Window
    {
        readonly NookApp app;
        readonly Border bar;
        IntPtr handle;
        readonly double Wd = 72, Th_ = 4;

        public LipWindow(NookApp a)
        {
            app = a;
            Wd = Math.Max(24, Math.Min(400, a.Cfg.D("pill.lipWidth", 72)));
            Th_ = Math.Max(2, Math.Min(12, a.Cfg.D("pill.lipThickness", 4)));
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; ShowActivated = false;
            Width = Wd * Th.K; Height = Th_ * Th.K; Title = "Nook Lippe";
            bool top = a.Top;
            bar = new Border
            {
                Background = Th.B(Th.A(Th.AccentC, 190)),
                CornerRadius = top ? new CornerRadius(0, 0, 3, 3) : new CornerRadius(3, 3, 0, 0)
            };
            Content = bar;
            SourceInitialized += (s, e) =>
            {
                handle = new WindowInteropHelper(this).Handle;
                N.AddExStyle(handle, 0x80 | 0x20 | 0x08000000);     // Toolfenster, klick-durchlässig, nicht aktivieren
            };
            Left = -10000; Top = -10000;
        }

        public void Set(bool show, Rect pill)
        {
            if (!show) { if (IsVisible) Hide(); return; }
            var mon = app.MonitorDip();
            Left = Math.Round(pill.X + pill.Width / 2 - Wd * Th.K / 2);
            Top = app.Top ? mon.Top : mon.Bottom - Th_ * Th.K;
            if (!IsVisible)
            {
                Opacity = 0;
                Show();
                BeginAnimation(OpacityProperty, new DoubleAnimation(1, TimeSpan.FromMilliseconds(200)));
            }
            KeepTopmost();
        }

        public void KeepTopmost()
        {
            if (IsVisible && handle != IntPtr.Zero) N.SetWindowPos(handle, N.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
        }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Fokussierbare Fläche für Editor-Elemente (Tastatur: Tab, Leertaste, Pfeile)
    // -----------------------------------------------------------------
    public class FocusBox : Border
    {
        public FocusBox()
        {
            Focusable = true; FocusVisualStyle = null;
            BorderThickness = new Thickness(1.5); BorderBrush = Brushes.Transparent;
            GotKeyboardFocus += (s, e) => BorderBrush = Th.Accent;
            LostKeyboardFocus += (s, e) => BorderBrush = Brushes.Transparent;
            PreviewMouseLeftButtonDown += (s, e) => Focus();
        }
    }

    class WinOwner : System.Windows.Forms.IWin32Window
    {
        public IntPtr Handle { get; set; }
    }

    // -----------------------------------------------------------------
    //  Eingebauter Einstellungs-Editor mit Live-Vorschau
    //  Wird nur beim Öffnen erzeugt und beim Schließen komplett verworfen.
    // -----------------------------------------------------------------
    public class EditorWindow : Window
    {
        public static EditorWindow Current;

        // Einstellungen: auf großen Bildschirmen (4K & Co.) etwas größer als der Rest,
        // auf Full HD normal. Das Fenster bleibt immer innerhalb von 90 % des Bildschirms.
        static double kE
        {
            get
            {
                double h = NookApp.I != null ? NookApp.I.MonitorDip().Height : 1080;
                double extra = h >= 1300 ? 1.15 : 1.0;
                return Th.K * extra;
            }
        }

        static readonly string[][] TabNames =
        {
            new[] { "controls", "Steuerung" }, new[] { "today", "Heute" }, new[] { "apps", "Apps" },
            new[] { "taskbar", "Taskbar" }, new[] { "clipboard", "Zwischenablage" }, new[] { "power", "Power" }
        };

        static readonly string[][] Presets =
        {
            new[] { "Gold", "#D4B266", "#0B0B0C", "#161618", "#232326", "#F2F2F0", "#8E8E93", "#0B0B0C" },
            new[] { "Ozean", "#5AA9FF", "#0A0D12", "#121821", "#1B2430", "#EEF3F8", "#8692A3", "#0A0D12" },
            new[] { "Mint", "#6FD6A8", "#0A0F0D", "#121A16", "#1B2621", "#EEF5F1", "#87968F", "#0A0F0D" },
            new[] { "Rosé", "#F09AB0", "#100B0D", "#1B1316", "#271C20", "#F7EEF1", "#9A8A90", "#100B0D" },
            new[] { "Lavendel", "#A99BFF", "#0C0B12", "#15131D", "#201D2B", "#F1EFF8", "#8F8A9E", "#0C0B12" },
            new[] { "Grafit", "#E8E8E8", "#0E0E0E", "#181818", "#242424", "#F2F2F2", "#8A8A8A", "#0E0E0E" },
            new[] { "Hell", "#2F6FEB", "#F6F6F4", "#ECECE9", "#E1E1DD", "#161616", "#6E6E73", "#F6F6F4" },
            new[] { "Aurora", "#7FE0D0", "#0A0F14", "#131B22", "#1C2630", "#EEF6F6", "#8A9AA3", "#0A0F14", "#1A1430" },
            new[] { "Sunset", "#FF9E6B", "#140C10", "#1E1418", "#2A1C22", "#F8EEEA", "#A08E8A", "#140C10", "#2A1420" },
            new[] { "Nebel", "#C9C2FF", "#101018", "#191924", "#242433", "#F1F0F8", "#908EA3", "#101018", "#18242C" }
        };

        // Bereiche der Seitenleiste: id, Name, Symbol
        static readonly string[][] Cats =
        {
            new[] { "general", "Allgemein", "\uE713" },
            new[] { "pill", "Pille", "\uE7F4" },
            new[] { "panel", "Panel", "\uE8A9" },
            new[] { "design", "Design", "\uE790" },
            new[] { "position", "Position", "\uE707" },
            new[] { "tabs", "Tabs", "\uE8FD" },
            new[] { "info", "Info", "\uE946" }
        };

        class Sec
        {
            public Border Box; public TextBlock Head; public string Cat, Title, Name;
            public List<KeyValuePair<UIElement, string>> Items = new List<KeyValuePair<UIElement, string>>();
        }

        readonly NookApp app;
        readonly Rect fromScreen;
        Dictionary<string, object> draft;
        bool dirty, closing, saved, confirmDiscard, confirmReset;
        readonly double EW, EH;
        const double M = 34;
        readonly Border shell;
        readonly Grid content;
        readonly StackPanel form;
        readonly Border pvHost;
        readonly Image logoImg;
        readonly TextBlock status, cancelTx, resetTx;
        readonly StackPanel pvSeg;
        readonly ScrollViewer formScroll;
        readonly StackPanel catSp;
        readonly TextBox searchTb;
        readonly TextBlock catTitle, noHits;
        string cat = "general";
        readonly List<Sec> secs = new List<Sec>();
        readonly Dictionary<UIElement, string> searchOf = new Dictionary<UIElement, string>();
        readonly List<KeyValuePair<string, FocusBox>> catBtns = new List<KeyValuePair<string, FocusBox>>();
        string pvMode = "panel", pvTab = "controls";
        DispatcherTimer pvT;
        DropShadowEffect shadow;
        IntPtr hwnd;

        public EditorWindow(NookApp a, Rect from)
        {
            app = a; fromScreen = from;
            Current = this;
            draft = Json.Parse(Json.Write(a.Cfg.Root)) as Dictionary<string, object> ?? new Dictionary<string, object>();

            var mon = a.MonitorDip();
            double k = kE;
            EW = Math.Min(1320, mon.Width / k * 0.9);
            EH = Math.Min(820, mon.Height / k * 0.9);
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize;
            Width = (EW + 2 * M) * k; Height = (EH + 2 * M) * k; Title = "Nook Einstellungen";
            UseLayoutRounding = true; FontFamily = Th.Font;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            double x = mon.Left + (mon.Width - EW * k) / 2;
            double y = a.Top ? mon.Top + Math.Max(a.Offset, 8) : mon.Bottom - Math.Max(a.Offset, 8) - EH * k;
            Left = Math.Round(x - M * k); Top = Math.Round(y - M * k);

            shell = new Border
            {
                Background = Th.Bg, CornerRadius = new CornerRadius(Math.Min(28, a.Cfg.D("panel.radius", 24) + 4)),
                Margin = new Thickness(M), Width = EW, Height = EH
            };
            shell.Effect = shadow = new DropShadowEffect { BlurRadius = 30, ShadowDepth = 6, Direction = 270, Opacity = 0, Color = Colors.Black };
            content = U.G(new[] { U.Px(64), U.Star(1) }, null);
            content.Opacity = 0;
            shell.Child = content;
            var root = new Grid { LayoutTransform = new ScaleTransform(k, k) };
            root.Children.Add(shell);
            Content = root;

            // Kopfzeile
            var head = U.G(null, new[] { U.Auto, U.Px(12), U.Auto, U.Px(16), U.Star(1), U.Auto });
            head.Margin = new Thickness(22, 14, 18, 0);
            logoImg = new Image { Width = 30, Height = 30 };
            U.Put(head, logoImg, 0, 0);
            var title = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            title.Children.Add(U.T("Nook", 17, Th.Text, FontWeights.Bold));
            title.Children.Add(U.T(Loc.Tr("  ·  Einstellungen"), 17, Th.Muted, FontWeights.SemiBold));
            var ver = U.T("   v" + NookApp.Version, 11.5, Th.Muted, FontWeights.SemiBold); ver.Margin = new Thickness(0, 4, 0, 0);
            title.Children.Add(ver);
            U.Put(head, title, 0, 2);
            status = U.T("", 12.5, Th.Accent, FontWeights.SemiBold);
            U.Put(head, status, 0, 4);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            resetTx = U.T(Loc.Tr("Standard"), 13, Th.Text, FontWeights.SemiBold);
            btns.Children.Add(Button(resetTx, false, ResetClick));
            btns.Children.Add(Button(U.T(Loc.Tr("Config-Ordner"), 13, Th.Text, FontWeights.SemiBold), false, () => app.OpenConfigFolder()));
            cancelTx = U.T(Loc.Tr("Abbrechen"), 13, Th.Text, FontWeights.SemiBold);
            btns.Children.Add(Button(cancelTx, false, Cancel));
            btns.Children.Add(Button(U.T(Loc.Tr("Speichern"), 13, Th.AccentText, FontWeights.Bold), true, Save));
            U.Put(head, btns, 0, 5);
            U.Put(content, head, 0, 0);

            // Links Suche + Bereiche, Mitte Optionen, rechts Vorschau
            double formW = Math.Max(330, Math.Min(480, (EW - 36 - 230) * 0.5));
            var body = U.G(null, new[] { U.Px(196), U.Px(16), U.Px(formW), U.Px(18), U.Star(1) });
            body.Margin = new Thickness(18, 4, 18, 18);

            var side = U.G(new[] { U.Auto, U.Px(14), U.Star(1), U.Auto }, null);
            searchTb = new TextBox();
            var sph = U.T(Loc.Tr("Suchen …"), 13, Th.Muted); sph.IsHitTestVisible = false; sph.Margin = new Thickness(1, 0, 0, 0);
            var sInner = new Grid(); sInner.Children.Add(sph); sInner.Children.Add(searchTb);
            var sg = U.G(null, new[] { U.Px(22), U.Star(1) });
            U.Put(sg, U.I("", 12, Th.Muted), 0, 0);
            U.Put(sg, sInner, 0, 1);
            U.Put(side, Field(searchTb, sg, 0), 0, 0);
            searchTb.TextChanged += (s, e) =>
            {
                sph.Visibility = searchTb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
                ApplyFilter();
                if (formScroll != null) formScroll.ScrollToTop();
            };
            searchTb.PreviewKeyDown += (s, e) => { if (e.Key == Key.Down || e.Key == Key.Enter) { FocusFirst(); e.Handled = true; } };
            catSp = new StackPanel();
            U.Put(side, catSp, 2, 0);
            var keys = U.T(Loc.Tr("Strg+F  Suchen") + "\n" + Loc.Tr("Strg+Bild↑/↓  Bereich"), 11, Th.Muted);
            keys.TextTrimming = TextTrimming.None; keys.Margin = new Thickness(8, 8, 0, 2);
            U.Put(side, keys, 3, 0);
            U.Put(body, side, 0, 0);

            var mid = U.G(new[] { U.Auto, U.Star(1) }, null);
            catTitle = U.T("", 20, Th.Text, FontWeights.Bold); catTitle.Margin = new Thickness(4, 0, 0, 12);
            U.Put(mid, catTitle, 0, 0);
            form = new StackPanel();
            formScroll = new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, Focusable = false, PanningMode = PanningMode.VerticalOnly };
            U.Put(mid, formScroll, 1, 0);
            noHits = U.T(Loc.Tr("Nichts gefunden."), 13, Th.Muted);
            noHits.Margin = new Thickness(4, 4, 0, 0); noHits.VerticalAlignment = VerticalAlignment.Top; noHits.Visibility = Visibility.Collapsed;
            U.Put(mid, noHits, 1, 0);
            U.Put(body, mid, 0, 2);

            var pv = U.G(new[] { U.Auto, U.Px(12), U.Star(1) }, null);
            var pvHead = U.G(null, new[] { U.Star(1), U.Auto });
            U.Put(pvHead, U.T(Loc.Tr("Vorschau").ToUpper(Th.Cul), 11.5, Th.Muted, FontWeights.Bold), 0, 0);
            pvSeg = new StackPanel { Orientation = Orientation.Horizontal };
            U.Put(pvHead, pvSeg, 0, 1);
            U.Put(pv, pvHead, 0, 0);
            pvHost = new Border { CornerRadius = new CornerRadius(14), ClipToBounds = true };
            U.Put(pv, pvHost, 2, 0);
            var pvCard = new Border { Background = Th.Surface, CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Child = pv };
            U.Put(body, pvCard, 0, 4);
            U.Put(content, body, 1, 0);

            cat = a.Store.Str("editorCat", "general");
            if (!Cats.Any(q => q[0] == cat)) cat = "general";
            if (cat == "pill") pvMode = "pill";
            BuildPvSeg();
            BuildCats();
            BuildForm();
            RebuildPreview();

            PreviewKeyDown += (s, e) =>
            {
                bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                if (ctrl && e.Key == Key.S) { Save(); e.Handled = true; }
                else if (ctrl && e.Key == Key.F) { searchTb.Focus(); Keyboard.Focus(searchTb); searchTb.SelectAll(); e.Handled = true; }
                else if (ctrl && (e.Key == Key.PageDown || e.Key == Key.PageUp))
                {
                    int i = Array.FindIndex(Cats, q => q[0] == cat) + (e.Key == Key.PageDown ? 1 : -1);
                    SetCat(Cats[(i + Cats.Length) % Cats.Length][0]);
                    FocusFirst();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape && searchTb.Text.Length > 0) { searchTb.Text = ""; FocusCat(); e.Handled = true; }
                else if (e.Key == Key.Escape) { Cancel(); e.Handled = true; }
            };
            SourceInitialized += (s, e) => { hwnd = new WindowInteropHelper(this).Handle; N.AddExStyle(hwnd, 0x80); };
            Closed += (s, e) => AfterClose();
        }

        // ---------------- Öffnen / Schließen ----------------
        Rect RelToShell(Rect screen)
        {
            double k = kE;
            return new Rect((screen.X - (Left + M * k)) / k, (screen.Y - (Top + M * k)) / k, Math.Max(1, screen.Width / k), Math.Max(1, screen.Height / k));
        }

        public void OpenAnimated()
        {
            var fr = RelToShell(fromScreen);
            var clip = new RectangleGeometry(fr, Math.Min(fr.Height / 2, 24), Math.Min(fr.Height / 2, 24));
            shell.Clip = clip;
            Show();
            N.SetWindowPos(hwnd, N.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            Activate();
            N.ForceForeground(hwnd);
            int ms = Math.Max(1, (int)(app.Cfg.D("panel.animationMs", 240) * 1.25));
            var ease = new QuarticEase { EasingMode = EasingMode.EaseOut };
            var dur = TimeSpan.FromMilliseconds(ms);
            double r = shell.CornerRadius.TopLeft;
            var ra = new RectAnimation(fr, new Rect(0, 0, EW, EH), dur) { EasingFunction = ease };
            ra.Completed += (s, e) =>
            {
                if (closing) return;
                shell.Clip = null;
                shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0.45, TimeSpan.FromMilliseconds(140)));
                FocusFirst();
            };
            clip.BeginAnimation(RectangleGeometry.RectProperty, ra);
            var rad = new DoubleAnimation(r, dur) { EasingFunction = ease };
            clip.BeginAnimation(RectangleGeometry.RadiusXProperty, rad);
            clip.BeginAnimation(RectangleGeometry.RadiusYProperty, rad);
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(ms * 0.7)) { BeginTime = TimeSpan.FromMilliseconds(ms * 0.25) });
        }

        void FocusFirst()
        {
            var first = FindFocusable(form);
            if (first != null) { first.Focus(); Keyboard.Focus(first); }
        }

        static IInputElement FindFocusable(DependencyObject d)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                var ui = c as UIElement;
                if (ui != null && ui.Focusable && ui.IsVisible && (c is FocusBox || c is TextBox)) return ui;
                var r = FindFocusable(c);
                if (r != null) return r;
            }
            return null;
        }

        void CloseAnimated()
        {
            if (closing) return;
            closing = true;
            var pr = app.PillRect();
            var target = RelToShell(new Rect(pr.X, pr.Y, pr.Width, pr.Height));
            var clip = new RectangleGeometry(new Rect(0, 0, EW, EH), shell.CornerRadius.TopLeft, shell.CornerRadius.TopLeft);
            shell.Clip = clip;
            shadow.BeginAnimation(DropShadowEffect.OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(80)));
            int ms = Math.Max(10, (int)(app.Cfg.D("panel.animationMs", 240) * 0.9));
            var ease = new CubicEase { EasingMode = EasingMode.EaseIn };
            var dur = TimeSpan.FromMilliseconds(ms);
            content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, TimeSpan.FromMilliseconds(ms * 0.45)));
            var ra = new RectAnimation(target, dur) { EasingFunction = ease };
            ra.Completed += (s, e) => Close();
            clip.BeginAnimation(RectangleGeometry.RectProperty, ra);
            var rad = new DoubleAnimation(pr.Height / 2 / kE, dur) { EasingFunction = ease };
            clip.BeginAnimation(RectangleGeometry.RadiusXProperty, rad);
            clip.BeginAnimation(RectangleGeometry.RadiusYProperty, rad);
        }

        void AfterClose()
        {
            if (Current == this) Current = null;
            if (pvT != null) pvT.Stop();
            pvT = null;
            if (form != null) form.Children.Clear();
            if (pvHost != null) pvHost.Child = null;
            draft = null;
            var a = app;
            a.Dispatcher.BeginInvoke(new Action(() =>
            {
                if (saved) a.Reload(); else if (a.Pill != null) a.Pill.HideForPanel(false);
                NookApp.Trim();
            }), DispatcherPriority.Background);
        }

        void Save()
        {
            if (closing) return;
            if (dirty)
            {
                S2("pill.visible", GS("pill.mode", "always") != "hidden");
                app.SaveConfig(draft);
                saved = true;
            }
            CloseAnimated();
        }

        void Cancel()
        {
            if (closing) return;
            if (dirty && !confirmDiscard)
            {
                confirmDiscard = true;
                cancelTx.Text = Loc.Tr("Verwerfen?"); cancelTx.Foreground = Th.Accent;
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (s, e) => { t.Stop(); confirmDiscard = false; cancelTx.Text = Loc.Tr("Abbrechen"); cancelTx.Foreground = Th.Text; };
                t.Start();
                return;
            }
            CloseAnimated();
        }

        void ResetClick()
        {
            if (!confirmReset)
            {
                confirmReset = true;
                resetTx.Text = Loc.Tr("Wirklich?"); resetTx.Foreground = Th.Accent;
                var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                t.Tick += (s, e) => { t.Stop(); confirmReset = false; resetTx.Text = Loc.Tr("Standard"); resetTx.Foreground = Th.Text; };
                t.Start();
                return;
            }
            confirmReset = false; resetTx.Text = Loc.Tr("Standard"); resetTx.Foreground = Th.Text;
            draft = (Dictionary<string, object>)Json.Parse(Cfg.Defaults);
            BuildForm();
            Changed();
            FocusFirst();
        }

        // ---------------- Daten ----------------
        object G(string path)
        {
            object cur = draft;
            foreach (var p in path.Split('.'))
            {
                var d = cur as Dictionary<string, object>;
                if (d == null || !d.TryGetValue(p, out cur)) return null;
            }
            return cur;
        }

        string GS(string p, string d) { var v = G(p); return v == null ? d : Convert.ToString(v, CultureInfo.InvariantCulture); }
        double GD(string p, double d) { var v = G(p); if (v == null || v is string || v is bool) return d; try { return Convert.ToDouble(v, CultureInfo.InvariantCulture); } catch { return d; } }
        bool GB(string p, bool d) { var v = G(p); return v is bool ? (bool)v : d; }
        List<string> GL(string p) { return Json.Seq(G(p)).Select(x => Convert.ToString(x)).ToList(); }

        void S2(string path, object v)
        {
            var parts = path.Split('.');
            var cur = draft;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                object nx;
                var d = cur.TryGetValue(parts[i], out nx) ? nx as Dictionary<string, object> : null;
                if (d == null) { d = new Dictionary<string, object>(); cur[parts[i]] = d; }
                cur = d;
            }
            cur[parts[parts.Length - 1]] = v;
        }

        void S(string path, object v) { S2(path, v); Changed(); }

        void Changed()
        {
            dirty = true;
            status.Text = Loc.Tr("●  Nicht gespeichert  ·  Strg+S");
            if (pvT == null)
            {
                pvT = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(70) };
                pvT.Tick += (s, e) => { pvT.Stop(); RebuildPreview(); };
            }
            pvT.Stop(); pvT.Start();
        }

        // ---------------- Bausteine ----------------
        FocusBox Button(TextBlock label, bool primary, Action act)
        {
            label.HorizontalAlignment = HorizontalAlignment.Center;
            var b = new FocusBox
            {
                Child = label, CornerRadius = new CornerRadius(10), Padding = new Thickness(14, 7, 14, 8), Margin = new Thickness(8, 0, 0, 0),
                Background = primary ? Th.Accent : Th.Surface, Cursor = Cursors.Hand
            };
            var normal = b.Background;
            var hover = primary ? Th.AccentHover : Th.Hover;
            b.MouseEnter += (s, e) => b.Background = hover;
            b.MouseLeave += (s, e) => b.Background = normal;
            b.MouseLeftButtonUp += (s, e) => { act(); e.Handled = true; };
            b.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { act(); e.Handled = true; } };
            return b;
        }

        // Ein Kasten mit Überschrift; landet direkt im Formular und ist durchsuchbar
        Border Section(string cat, string title, params UIElement[] rows)
        {
            var sp = new StackPanel();
            var h = U.T(Loc.Tr(title).ToUpper(Th.Cul), 11.5, Th.Muted, FontWeights.Bold);
            h.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(h);
            var sec = new Sec { Cat = cat, Name = Loc.Tr(title), Head = h, Title = Loc.Tr(title) + " " + title + " " + CatName(cat) };
            foreach (var r in rows)
            {
                if (r == null) continue;
                sp.Children.Add(r);
                string st; searchOf.TryGetValue(r, out st);
                sec.Items.Add(new KeyValuePair<UIElement, string>(r, st ?? ""));
            }
            sec.Box = new Border { Background = Th.Surface, CornerRadius = new CornerRadius(18), Padding = new Thickness(18, 16, 18, 10), Margin = new Thickness(0, 0, 0, 12), Child = sp };
            secs.Add(sec);
            form.Children.Add(sec.Box);
            return sec.Box;
        }

        Grid Row(string label, string hint, UIElement ctl)
        {
            var g = U.G(null, new[] { U.Star(1), U.Auto });
            g.MinHeight = 40; g.Margin = new Thickness(0, 2, 0, 2);
            var l = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
            var lt = U.T(label, 13.5, Th.Text); lt.TextWrapping = TextWrapping.Wrap; lt.TextTrimming = TextTrimming.None;
            l.Children.Add(lt);
            if (!string.IsNullOrEmpty(hint))
            {
                var ht = U.T(hint, 11.5, Th.Muted); ht.TextWrapping = TextWrapping.Wrap; ht.TextTrimming = TextTrimming.None;
                l.Children.Add(ht);
            }
            U.Put(g, l, 0, 0);
            var c = ctl as FrameworkElement;
            if (c != null) c.VerticalAlignment = VerticalAlignment.Center;
            U.Put(g, ctl, 0, 1);
            searchOf[g] = label + " " + (hint ?? "");
            return g;
        }

        UIElement Tagged(UIElement el, string text) { searchOf[el] = text ?? ""; return el; }

        UIElement Note(string text)
        {
            var t = U.T(text, 11.5, Th.Muted); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None;
            t.Margin = new Thickness(0, 0, 0, 8);
            return Tagged(t, text);
        }

        // Optionen mit übersetztem Text: "wert:Text" (Seg) bzw. "wert|Text" (Auswahl)
        static string[] O(params string[] opts) { return opts.Select(o => { int i = o.IndexOf(':'); return o.Substring(0, i + 1) + Loc.Tr(o.Substring(i + 1)); }).ToArray(); }
        static List<string[]> LT(params string[] pairs) { return pairs.Select(p => { int i = p.IndexOf('|'); return new[] { p.Substring(0, i), Loc.Tr(p.Substring(i + 1)) }; }).ToList(); }

        FocusBox Toggle(string path, Action after)
        {
            var knob = new Border { Width = 17, Height = 17, CornerRadius = new CornerRadius(8.5), HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var box = new FocusBox { Width = 44, Height = 26, CornerRadius = new CornerRadius(13), Cursor = Cursors.Hand, Child = knob };
            Action render = () =>
            {
                bool on = GB(path, false);
                box.Background = on ? Th.Accent : Th.Surface2;
                knob.Background = on ? Th.AccentText : Th.B(Color.FromRgb(217, 217, 220));
                knob.Margin = new Thickness(on ? 21.5 : 3, 0, 0, 0);
            };
            Action flip = () => { S(path, !GB(path, false)); render(); if (after != null) after(); };
            box.MouseLeftButtonUp += (s, e) => { flip(); e.Handled = true; };
            box.KeyDown += (s, e) => { if (e.Key == Key.Space || e.Key == Key.Enter) { flip(); e.Handled = true; } };
            render();
            return box;
        }

        FocusBox Seg(string path, string[] opts, Action after)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            var box = new FocusBox { Background = Th.Surface2, CornerRadius = new CornerRadius(10), Padding = new Thickness(3), Child = sp };
            var vals = opts.Select(o => o.Split(':')[0]).ToArray();
            var items = new List<KeyValuePair<Border, TextBlock>>();
            Action render = () =>
            {
                string cur = GS(path, vals[0]);
                for (int i = 0; i < items.Count; i++)
                {
                    bool on = vals[i] == cur;
                    items[i].Key.Background = on ? Th.AccentSoft : Brushes.Transparent;
                    items[i].Value.Foreground = on ? Th.Accent : Th.Muted;
                }
            };
            Action<int> pick = i => { S(path, vals[i]); render(); if (after != null) after(); };
            for (int i = 0; i < opts.Length; i++)
            {
                int idx = i;
                var t = U.T(opts[i].Split(':')[1], 12.5, Th.Muted, FontWeights.SemiBold);
                var b = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(11, 5, 11, 6), Child = t, Cursor = Cursors.Hand };
                b.MouseLeftButtonUp += (s, e) => { pick(idx); e.Handled = true; };
                items.Add(new KeyValuePair<Border, TextBlock>(b, t));
                sp.Children.Add(b);
            }
            box.KeyDown += (s, e) =>
            {
                int cur = Array.IndexOf(vals, GS(path, vals[0]));
                if (e.Key == Key.Left) { pick(Math.Max(0, cur - 1)); e.Handled = true; }
                else if (e.Key == Key.Right) { pick(Math.Min(vals.Length - 1, cur + 1)); e.Handled = true; }
            };
            render();
            return box;
        }

        UIElement Slider(string path, double min, double max, double step, string unit, bool pct)
        {
            const double TW = 150;
            var track = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.Surface2, VerticalAlignment = VerticalAlignment.Center };
            var fill = new Border { Height = 4, CornerRadius = new CornerRadius(2), Background = Th.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var knob = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(7), Background = Th.Accent, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid { Width = TW, Height = 24, Background = Brushes.Transparent };
            g.Children.Add(track); g.Children.Add(fill); g.Children.Add(knob);
            var box = new FocusBox { Child = g, CornerRadius = new CornerRadius(8), Padding = new Thickness(4, 0, 4, 0), Cursor = Cursors.Hand };
            var val = U.T("", 12.5, Th.Muted, FontWeights.SemiBold);
            val.Width = 62; val.TextAlignment = TextAlignment.Right;
            Action render = () =>
            {
                double v = Math.Max(min, Math.Min(max, GD(path, min)));
                double t = (v - min) / (max - min);
                fill.Width = Math.Max(0, TW * t);
                knob.Margin = new Thickness(Math.Max(0, Math.Min(TW - 14, TW * t - 7)), 0, 0, 0);
                val.Text = pct ? Math.Round(v * 100) + " %" : v.ToString("0.##", Th.Cul) + (unit != null ? " " + unit : "");
            };
            Action<double> setV = v =>
            {
                v = Math.Round(v / step) * step;
                v = Math.Max(min, Math.Min(max, Math.Round(v, 4)));
                if (Math.Abs(v - GD(path, min)) < 1e-9) return;
                S(path, v); render();
            };
            bool drag = false;
            g.MouseLeftButtonDown += (s, e) => { drag = true; g.CaptureMouse(); setV(min + Math.Max(0, Math.Min(1, e.GetPosition(g).X / TW)) * (max - min)); e.Handled = true; };
            g.MouseMove += (s, e) => { if (drag) setV(min + Math.Max(0, Math.Min(1, e.GetPosition(g).X / TW)) * (max - min)); };
            g.MouseLeftButtonUp += (s, e) => { drag = false; g.ReleaseMouseCapture(); };
            box.KeyDown += (s, e) =>
            {
                double v = GD(path, min);
                switch (e.Key)
                {
                    case Key.Left: case Key.Down: setV(v - step); e.Handled = true; break;
                    case Key.Right: case Key.Up: setV(v + step); e.Handled = true; break;
                    case Key.PageDown: setV(v - step * 10); e.Handled = true; break;
                    case Key.PageUp: setV(v + step * 10); e.Handled = true; break;
                    case Key.Home: setV(min); e.Handled = true; break;
                    case Key.End: setV(max); e.Handled = true; break;
                }
            };
            box.MouseWheel += (s, e) => { setV(GD(path, min) + (e.Delta > 0 ? step : -step)); e.Handled = true; };
            render();
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(box); sp.Children.Add(val);
            return sp;
        }

        Border Field(TextBox tb, double width) { return Field(tb, tb, width); }

        // Rahmen um ein Eingabefeld; "child" ist das, was im Rahmen steht (Textfeld allein oder mit Platzhalter)
        Border Field(TextBox tb, UIElement child, double width)
        {
            tb.Background = Brushes.Transparent; tb.BorderThickness = new Thickness(0); tb.Foreground = Th.Text; tb.CaretBrush = Th.Accent;
            tb.FontSize = 13; tb.FontFamily = Th.Font; tb.SelectionBrush = Th.Accent; tb.FocusVisualStyle = null; tb.Padding = new Thickness(0);
            tb.VerticalContentAlignment = VerticalAlignment.Center;
            var b = new Border { Background = Th.Surface2, CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 6, 10, 6), BorderThickness = new Thickness(1.5), BorderBrush = Brushes.Transparent };
            b.Child = child;
            if (width > 0) b.Width = width;
            tb.GotKeyboardFocus += (s, e) => b.BorderBrush = Th.Accent;
            tb.LostKeyboardFocus += (s, e) => b.BorderBrush = Brushes.Transparent;
            return b;
        }

        UIElement TextField(string path, string placeholder, double width)
        {
            var tb = new TextBox { Text = GS(path, "") };
            var ph = U.T(placeholder, 13, Th.Muted); ph.IsHitTestVisible = false; ph.Margin = new Thickness(1, 0, 0, 0);
            ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
            tb.TextChanged += (s, e) => { ph.Visibility = tb.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed; S(path, tb.Text); };
            var g = new Grid();
            g.Children.Add(ph);
            g.Children.Add(tb);
            return Field(tb, g, width);
        }

        // Farbe: Feld + Farbwähler. allowAuto: leer bzw. "auto" = automatisch
        UIElement ColorField(string path, bool allowAuto)
        {
            var sw = new FocusBox { Width = 36, Height = 30, CornerRadius = new CornerRadius(9), Cursor = Cursors.Hand, ToolTip = Loc.Tr("Farbe wählen (Enter)") };
            var tb = new TextBox { Width = 84, FontFamily = new FontFamily("Cascadia Mono, Consolas") };
            Action render = () =>
            {
                string v = GS(path, "").Trim();
                bool auto = allowAuto && v.Length == 0;
                if (auto)
                {
                    sw.Background = Th.Surface2;
                    var a = U.T("A", 11, Th.Muted, FontWeights.Bold); a.HorizontalAlignment = HorizontalAlignment.Center; a.VerticalAlignment = VerticalAlignment.Center;
                    sw.Child = a;
                }
                else { sw.Background = Th.B(Th.P(v, Colors.Black)); sw.Child = null; }
                if (!tb.IsKeyboardFocused) tb.Text = auto ? "Auto" : v.ToUpperInvariant();
            };
            Action pickDlg = () =>
            {
                var c = Th.P(GS(path, "#000000"), Colors.Black);
                using (var d = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true, Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B) })
                {
                    if (d.ShowDialog(new WinOwner { Handle = hwnd }) == System.Windows.Forms.DialogResult.OK)
                    {
                        S(path, string.Format("#{0:X2}{1:X2}{2:X2}", d.Color.R, d.Color.G, d.Color.B));
                        render();
                    }
                }
                Activate();
            };
            sw.MouseLeftButtonUp += (s, e) => { pickDlg(); e.Handled = true; };
            sw.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space) { pickDlg(); e.Handled = true; } };
            Action commit = () =>
            {
                string v = tb.Text.Trim();
                if (allowAuto && (v.Length == 0 || v.Equals("auto", StringComparison.OrdinalIgnoreCase)))
                {
                    if (GS(path, "").Trim().Length > 0) S(path, "");
                    render();
                    return;
                }
                if (!v.StartsWith("#")) v = "#" + v;
                if (Regex.IsMatch(v, "^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$")) { if (!v.Equals(GS(path, ""), StringComparison.OrdinalIgnoreCase)) S(path, v.ToUpperInvariant()); }
                render();
            };
            tb.LostKeyboardFocus += (s, e) => commit();
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { commit(); e.Handled = true; } };
            render();
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(sw);
            var f = Field(tb, 0); f.Margin = new Thickness(8, 0, 0, 0);
            sp.Children.Add(f);
            return sp;
        }

        FocusBox Choice(string path, Func<List<string[]>> options, double width)
        {
            var label = U.T("", 13, Th.Text, FontWeights.SemiBold);
            var g = U.G(null, new[] { U.Star(1), U.Px(20) });
            U.Put(g, label, 0, 0);
            U.Put(g, U.I("", 9, Th.Muted), 0, 1);
            var box = new FocusBox { Child = g, Background = Th.Surface2, CornerRadius = new CornerRadius(9), Padding = new Thickness(10, 6, 6, 6), Cursor = Cursors.Hand };
            if (width > 0) box.Width = width;
            Action render = () =>
            {
                string cur = GS(path, "");
                var o = options().FirstOrDefault(x => x[0] == cur);
                label.Text = o != null ? o[1] : (cur.Length == 0 ? "–" : cur);
            };
            Action open = () =>
            {
                var items = new List<MI>();
                string cur = GS(path, "");
                foreach (var o in options())
                {
                    var v = o[0];
                    items.Add(MI.Item(o[1], v == cur ? "" : null, () => { S(path, v); render(); box.Focus(); }));
                }
                var p = box.PointToScreen(new Point(0, box.ActualHeight + 14));
                double sc = NookApp.Scale();
                new MenuWindow(app, items, new Point(p.X / sc + 16, p.Y / sc)).Open();
            };
            box.MouseLeftButtonUp += (s, e) => { open(); e.Handled = true; };
            box.KeyDown += (s, e) => { if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Down) { open(); e.Handled = true; } };
            render();
            return box;
        }

        static List<string[]> L(params string[] pairs)
        {
            return pairs.Select(p => { int i = p.IndexOf('|'); return new[] { p.Substring(0, i), p.Substring(i + 1) }; }).ToList();
        }

        string TabName(string id)
        {
            var t = TabNames.FirstOrDefault(x => x[0] == id);
            return t != null ? Loc.Tr(t[1]) : id;
        }

        List<string[]> TabOptions(bool withLast)
        {
            var l = new List<string[]>();
            if (withLast) l.Add(new[] { "last", Loc.Tr("Zuletzt benutzter") });
            foreach (var t in GL("tabs")) l.Add(new[] { t, TabName(t) });
            return l;
        }

        // ---------------- Seitenleiste & Suche ----------------
        string CatName(string id) { var c = Cats.FirstOrDefault(x => x[0] == id); return c != null ? Loc.Tr(c[1]) : id; }

        bool IsCatOn(string id) { return searchTb != null && searchTb.Text.Trim().Length == 0 && id == cat; }

        void BuildCats()
        {
            catSp.Children.Clear(); catBtns.Clear();
            for (int i = 0; i < Cats.Length; i++)
            {
                string id = Cats[i][0];
                int idx = i;
                var g = U.G(null, new[] { U.Px(28), U.Star(1) });
                U.Put(g, U.I(Cats[i][2], 14, Th.Muted), 0, 0);
                U.Put(g, U.T(Loc.Tr(Cats[i][1]), 13.5, Th.Text, FontWeights.SemiBold), 0, 1);
                var b = new FocusBox { Child = g, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 3), Cursor = Cursors.Hand, Background = Brushes.Transparent };
                b.MouseEnter += (s, e) => { if (!IsCatOn(id)) b.Background = Th.Hover; };
                b.MouseLeave += (s, e) => PaintCats();
                b.MouseLeftButtonUp += (s, e) => { SetCat(id); e.Handled = true; };
                b.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Enter || e.Key == Key.Space || e.Key == Key.Right) { SetCat(id); FocusFirst(); e.Handled = true; }
                    else if (e.Key == Key.Down || e.Key == Key.Up)
                    {
                        int j = (idx + (e.Key == Key.Down ? 1 : -1) + Cats.Length) % Cats.Length;
                        SetCat(Cats[j][0]);
                        var nb = catBtns[j].Value; nb.Focus(); Keyboard.Focus(nb);
                        e.Handled = true;
                    }
                };
                catBtns.Add(new KeyValuePair<string, FocusBox>(id, b));
                catSp.Children.Add(b);
            }
            PaintCats();
        }

        void PaintCats()
        {
            foreach (var kv in catBtns)
            {
                bool on = IsCatOn(kv.Key);
                kv.Value.Background = on ? Th.AccentSoft : Brushes.Transparent;
                foreach (var ch in ((Grid)kv.Value.Child).Children)
                {
                    var t = ch as TextBlock;
                    if (t == null) continue;
                    t.Foreground = on ? Th.Accent : (Grid.GetColumn(t) == 0 ? Th.Muted : Th.Text);
                }
            }
        }

        void FocusCat()
        {
            var b = catBtns.FirstOrDefault(x => x.Key == cat).Value;
            if (b != null) { b.Focus(); Keyboard.Focus(b); }
        }

        void SetCat(string id)
        {
            cat = id;
            try { app.Store.Set("editorCat", id); } catch { }
            if (searchTb.Text.Length > 0) searchTb.Text = "";
            ApplyFilter();
            formScroll.ScrollToTop();
            string pm = id == "pill" ? "pill" : (id == "panel" || id == "design" || id == "tabs") ? "panel" : pvMode;
            if (pm != pvMode) { pvMode = pm; BuildPvSeg(); RebuildPreview(); }
        }

        // Ohne Suchtext: nur der gewählte Bereich. Mit Suchtext: alle passenden Zeilen aus allen Bereichen.
        void ApplyFilter()
        {
            if (searchTb == null || catTitle == null) return;
            string q = searchTb.Text.Trim().ToLowerInvariant();
            int hits = 0;
            foreach (var sc in secs)
            {
                if (q.Length == 0)
                {
                    sc.Box.Visibility = sc.Cat == cat ? Visibility.Visible : Visibility.Collapsed;
                    sc.Head.Text = sc.Name.ToUpper(Th.Cul);
                    foreach (var it in sc.Items) it.Key.Visibility = Visibility.Visible;
                    continue;
                }
                bool title = sc.Title.ToLowerInvariant().Contains(q);
                bool any = false;
                foreach (var it in sc.Items)
                {
                    bool m = title || it.Value.ToLowerInvariant().Contains(q);
                    it.Key.Visibility = m ? Visibility.Visible : Visibility.Collapsed;
                    if (m) { any = true; hits++; }
                }
                sc.Box.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
                sc.Head.Text = (CatName(sc.Cat) + "  ·  " + sc.Name).ToUpper(Th.Cul);
            }
            catTitle.Text = q.Length == 0 ? CatName(cat) : hits == 1 ? Loc.Tr("1 Treffer") : hits + Loc.Tr(" Treffer");
            noHits.Visibility = q.Length > 0 && hits == 0 ? Visibility.Visible : Visibility.Collapsed;
            PaintCats();
        }

        // Listen für die Reihenfolge-Editoren: id, Name, Symbol
        string[][] TabItems() { return TabNames.Select(t => new[] { t[0], Loc.Tr(t[1]), TabLook.Glyph(t[0]) }).ToArray(); }

        static string[][] TileItems()
        {
            return new[]
            {
                new[] { "wifi", Loc.Tr("WLAN"), "" }, new[] { "bluetooth", "Bluetooth", "" }, new[] { "audio", "Audio", "" },
                new[] { "night", Loc.Tr("Nachtmodus"), "" }, new[] { "dark", Loc.Tr("Dunkler Modus"), "" }, new[] { "mic", Loc.Tr("Mikrofon"), "" }
            };
        }

        static string[][] PowerItems()
        {
            return new[]
            {
                new[] { "lock", Loc.Tr("Sperren"), "" }, new[] { "sleep", Loc.Tr("Energie sparen"), "" }, new[] { "logoff", Loc.Tr("Abmelden"), "" },
                new[] { "restart", Loc.Tr("Neu starten"), "" }, new[] { "shutdown", Loc.Tr("Herunterfahren"), "" }
            };
        }

        static string[][] PillItems()
        {
            return new[]
            {
                new[] { "mic", Loc.Tr("Mikro (nur wenn stumm)"), "" }, new[] { "clock", Loc.Tr("Uhrzeit"), "" },
                new[] { "date", Loc.Tr("Datum"), "" }, new[] { "timer", Loc.Tr("Timer (nur wenn er läuft)"), "" },
                new[] { "media", Loc.Tr("Laufender Song"), "" }, new[] { "windows", Loc.Tr("Offene Apps"), "" }
            };
        }

        // ---------------- Formular ----------------
        void BuildForm()
        {
            form.Children.Clear(); secs.Clear(); searchOf.Clear();

            // ===== Allgemein =====
            Section("general", "Sprache & Größe",
                Row("Language · Sprache", null, Seg("language", new[] { "en:English", "de:Deutsch" }, null)),
                Row(Loc.Tr("Größe"), Loc.Tr("Automatisch passt sich an 4K & Co. an"), Choice("theme.scale", () => L(Loc.Tr("auto|Automatisch (") + Math.Round(app.ComputeScale(new Cfg { Root = new Dictionary<string, object>() }) * 100) + " %)",
                    "0.9|90 %", "1|100 %", "1.15|115 %", "1.3|130 %", "1.5|150 %", "1.75|175 %", "2|200 %"), 190)));
            Section("general", "Öffnen",
                Row(Loc.Tr("Win-Taste öffnet Nook"), Loc.Tr("Win+E, Win+L usw. bleiben"), Toggle("behavior.winKey", null)),
                Row(Loc.Tr("Tab bei Win-Taste"), null, Choice("behavior.winTab", () => { var l = TabOptions(false); l.Insert(0, new[] { "same", Loc.Tr("Wie Start-Tab") }); return l; }, 190)),
                Row(Loc.Tr("Zusätzliches Tastenkürzel"), Loc.Tr("öffnet und schließt Nook von überall"), Choice("behavior.hotkey", () => LT("|Keins",
                    "Ctrl+Space|Strg+Leertaste", "Ctrl+Alt+Space|Strg+Alt+Leertaste", "Ctrl+Shift+Space|Strg+Umschalt+Leertaste", "Alt+Space|Alt+Leertaste",
                    "Ctrl+Alt+N|Strg+Alt+N", "Ctrl+Shift+N|Strg+Umschalt+N", "F12|F12"), 210)),
                Row(Loc.Tr("Einfach lostippen"), Loc.Tr("Tippen ohne Suchfeld springt zur Suche"), Choice("behavior.typeToSearch", () => LT("apps|Zur App-Suche", "taskbar|Zur Fenster-Suche", "off|Aus"), 190)));
            Section("general", "System",
                Row(Loc.Tr("Windows-Taskleiste ausblenden"), Loc.Tr("Desktop-Icons bleiben, wo sie sind"), Toggle("behavior.hideWindowsTaskbar", null)),
                Row(Loc.Tr("Mit Windows starten"), Program.IsTest ? Loc.Tr("in der Testversion ohne Wirkung") : null, Toggle("behavior.autostart", null)));

            // ===== Pille =====
            Section("pill", "Anzeige",
                Row(Loc.Tr("Pille anzeigen"), Loc.Tr("Am Rand: nur ein Strich, Maus hin – Pille erscheint"), Seg("pill.mode", O("always:Immer", "lip:Am Rand", "hidden:Aus"), null)),
                Row(Loc.Tr("Bei Vollbild ausblenden"), Loc.Tr("Spiele, Videos, F11"), Toggle("pill.hideInFullscreen", null)),
                Row(Loc.Tr("Platz reservieren"), Loc.Tr("nur bei „Immer“: Fenster enden unter der Pille"), Toggle("pill.reserveSpace", null)));
            Section("pill", "Form",
                Row(Loc.Tr("Höhe"), null, Slider("pill.height", 24, 48, 1, "px", false)),
                Row(Loc.Tr("Schriftgröße"), null, Slider("pill.fontSize", 11, 17, 0.5, "px", false)),
                Row(Loc.Tr("Rundung"), Loc.Tr("100 % = ganz rund, 0 % = eckig"), Slider("pill.roundness", 0, 100, 5, "%", false)),
                Row(Loc.Tr("Rahmen"), Loc.Tr("Farbe und Breite unter Design bzw. Panel"), Toggle("pill.border", null)));
            Section("pill", "Inhalt",
                Note(Loc.Tr("Reihenfolge in der Pille – Strg+↑/↓ oder die Pfeile verschieben.")),
                OrderList("pill.order", PillItems(), 0, false),
                Row(Loc.Tr("Uhrzeit"), null, Toggle("pill.showClock", null)),
                Row(Loc.Tr("24-Stunden-Format"), null, Toggle("pill.clock24h", null)),
                Row(Loc.Tr("Sekunden anzeigen"), null, Toggle("pill.clockSeconds", null)),
                Row(Loc.Tr("Datum"), null, Toggle("pill.showDate", null)),
                Row(Loc.Tr("Datumsformat"), null, Seg("pill.dateFormat", O("short:Kurz", "long:Lang", "numeric:Zahlen"), null)),
                Row(Loc.Tr("Laufender Song"), null, Toggle("pill.showMedia", null)),
                Row(Loc.Tr("Max. Breite des Songs"), null, Slider("pill.mediaWidth", 100, 400, 10, "px", false)),
                Row(Loc.Tr("Mikro stumm anzeigen"), Loc.Tr("rotes Symbol in der Pille"), Toggle("pill.showMicMuted", null)));
            Section("pill", "Offene Apps",
                Row(Loc.Tr("Offene Apps anzeigen"), Loc.Tr("Klick öffnet/minimiert, Mittelklick schließt"), Toggle("pill.showWindows", null)),
                Row(Loc.Tr("Fenster einer App zusammenfassen"), Loc.Tr("aus = jedes Fenster bekommt ein eigenes Icon"), Toggle("pill.groupWindows", null)),
                Row(Loc.Tr("Max. App-Icons"), null, Slider("pill.maxWindowIcons", 3, 20, 1, null, false)),
                Row(Loc.Tr("Icon-Größe"), Loc.Tr("0 = automatisch"), Slider("pill.iconSize", 0, 32, 1, "px", false)));
            Section("pill", "Maus",
                Row(Loc.Tr("Klick auf die Pille öffnet"), null, Choice("pill.clickTab", () => { var l = TabOptions(false); l.Insert(0, new[] { "start", Loc.Tr("Start-Tab") }); return l; }, 190)),
                Row(Loc.Tr("Mausrad über der Pille"), null, Seg("pill.wheel", O("volume:Lautstärke", "none:Nichts"), null)),
                Row(Loc.Tr("Mittelklick auf ein App-Icon"), null, Seg("pill.middleClick", O("close:Schließen", "none:Nichts"), null)));
            Section("pill", "Am Rand (Strich)",
                Note(Loc.Tr("Gilt, wenn „Pille anzeigen“ auf „Am Rand“ steht.")),
                Row(Loc.Tr("Strichbreite"), null, Slider("pill.lipWidth", 24, 300, 4, "px", false)),
                Row(Loc.Tr("Strichdicke"), null, Slider("pill.lipThickness", 2, 12, 1, "px", false)),
                Row(Loc.Tr("Ausblenden nach"), Loc.Tr("wenn die Maus die Pille verlässt"), Slider("pill.lipHideMs", 150, 3000, 50, "ms", false)));

            // ===== Panel =====
            Section("panel", "Größe & Form",
                Row(Loc.Tr("Breite"), null, Slider("panel.width", 560, 1200, 10, "px", false)),
                Row(Loc.Tr("Höhe"), null, Slider("panel.height", 380, 720, 10, "px", false)),
                Row(Loc.Tr("Eckenradius"), null, Slider("panel.radius", 0, 36, 1, "px", false)),
                Row(Loc.Tr("Rahmenbreite"), Loc.Tr("0 = kein Rahmen"), Slider("panel.borderWidth", 0, 4, 0.5, "px", false)));
            Section("panel", "Tab-Leiste",
                Row(Loc.Tr("Tab-Stil"), null, Seg("panel.tabStyle", O("pill:Pille", "underline:Linie", "text:Nur Text"), null)),
                Row(Loc.Tr("Tab-Beschriftung"), null, Seg("panel.tabIcons", O("text:Text", "icons:Symbole", "both:Beides"), null)),
                Row(Loc.Tr("Tabs ausrichten"), null, Seg("panel.tabAlign", O("left:Links", "center:Mitte"), null)),
                Row(Loc.Tr("Uhr in der Tab-Leiste"), null, Toggle("panel.showClock", null)),
                Row(Loc.Tr("Hilfe-Knopf"), Loc.Tr("F1 geht immer"), Toggle("panel.showHelpButton", null)),
                Row(Loc.Tr("Menü-Knopf"), Loc.Tr("Rechtsklick geht immer"), Toggle("panel.showMenuButton", null)));
            Section("panel", "Verhalten",
                Row(Loc.Tr("Start-Tab"), Loc.Tr("Tab beim Öffnen"), Choice("panel.startTab", () => TabOptions(true), 190)),
                Row(Loc.Tr("Schließen beim Wegklicken"), null, Toggle("panel.closeOnBlur", null)));
            Section("panel", "Animation & Schatten",
                Row(Loc.Tr("Öffnen-Effekt"), null, Seg("panel.animStyle", O("morph:Morph", "fade:Blende", "slide:Gleiten", "none:Aus"), null)),
                Row(Loc.Tr("Dauer"), Loc.Tr("0 = sofort"), Slider("panel.animationMs", 0, 600, 10, "ms", false)),
                Row(Loc.Tr("Schatten"), null, Toggle("panel.shadow", null)),
                Row(Loc.Tr("Schattenstärke"), null, Slider("panel.shadowStrength", 0, 1, 0.05, null, true)));

            // ===== Design =====
            var presets = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var pr in Presets)
            {
                var p = pr;
                var sw = new Border { Width = 18, Height = 18, CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 8, 0), BorderBrush = Th.Faint, BorderThickness = new Thickness(1) };
                sw.Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Th.P(p[1], Colors.Gray), 0), new GradientStop(Th.P(p[1], Colors.Gray), 0.5),
                    new GradientStop(Th.P(p[2], Colors.Black), 0.5), new GradientStop(Th.P(p.Length > 8 ? p[8] : p[2], Colors.Black), 1)
                }, 45);
                var spp = new StackPanel { Orientation = Orientation.Horizontal };
                spp.Children.Add(sw); spp.Children.Add(U.T(Loc.Tr(p[0]), 12.5, Th.Text, FontWeights.SemiBold));
                var b = Button(new TextBlock(), false, () =>
                {
                    S2("theme.accent", p[1]); S2("theme.background", p[2]); S2("theme.surface", p[3]); S2("theme.surface2", p[4]);
                    S2("theme.text", p[5]); S2("theme.muted", p[6]); S2("theme.pillBackground", p[7]);
                    S2("theme.gradient", p.Length > 8);
                    if (p.Length > 8) S2("theme.background2", p[8]);
                    RebuildKeepScroll(); Changed();
                });
                b.Child = spp; b.Padding = new Thickness(6, 5, 11, 5); b.Margin = new Thickness(0, 0, 6, 6); b.Background = Th.Surface2;
                presets.Children.Add(b);
            }
            Section("design", "Vorlagen", Tagged(presets, Loc.Tr("Vorlagen") + " " + string.Join(" ", Presets.Select(x => x[0]))));
            Section("design", "Farben",
                Row(Loc.Tr("Akzent"), null, ColorField("theme.accent", false)),
                Row(Loc.Tr("Hintergrund"), null, ColorField("theme.background", false)),
                Row(Loc.Tr("Verlauf"), Loc.Tr("Hintergrund läuft schräg in eine zweite Farbe"), Toggle("theme.gradient", null)),
                Row(Loc.Tr("Verlaufsfarbe"), null, ColorField("theme.background2", false)),
                Row(Loc.Tr("Fläche"), Loc.Tr("Kacheln, Karten"), ColorField("theme.surface", false)),
                Row(Loc.Tr("Fläche 2"), Loc.Tr("Knöpfe, Felder"), ColorField("theme.surface2", false)),
                Row("Text", null, ColorField("theme.text", false)),
                Row(Loc.Tr("Text gedämpft"), null, ColorField("theme.muted", false)),
                Row(Loc.Tr("Pille"), Loc.Tr("Auto = wie Hintergrund"), ColorField("theme.pillBackground", true)),
                Row(Loc.Tr("Rahmenfarbe"), Loc.Tr("Auto = leicht aus der Textfarbe"), ColorField("theme.border", true)),
                Row(Loc.Tr("Deckkraft"), null, Slider("theme.opacity", 0.5, 1, 0.01, null, true)));
            Section("design", "Karten",
                Row(Loc.Tr("Kartenstil"), Loc.Tr("Kacheln, Suchfelder, Bereiche"), Seg("theme.cardStyle", O("filled:Gefüllt", "outline:Umriss", "flat:Flach"), null)),
                Row(Loc.Tr("Kartenradius"), null, Slider("theme.cardRadius", 0, 30, 1, "px", false)));
            Section("design", "Schrift",
                Row(Loc.Tr("Schrift"), null, Choice("theme.font", () => L("Segoe UI Variable Display|Segoe UI Variable Display", "Segoe UI Variable Text|Segoe UI Variable Text",
                    "Segoe UI|Segoe UI", "Bahnschrift|Bahnschrift", "Cascadia Code|Cascadia Code", "Inter|Inter", "Arial|Arial", "Calibri|Calibri", "Consolas|Consolas"), 210)));

            // ===== Position =====
            Section("position", "Position",
                Row(Loc.Tr("Rand"), null, Seg("position.edge", O("top:Oben", "bottom:Unten"), null)),
                Row(Loc.Tr("Ausrichtung"), null, Seg("position.align", O("left:Links", "center:Mitte", "right:Rechts"), null)),
                Row(Loc.Tr("Abstand zum Rand"), Loc.Tr("0 = bündig, wie eine Notch"), Slider("position.offset", 0, 40, 1, "px", false)),
                Row(Loc.Tr("Seitenabstand"), Loc.Tr("nur bei links/rechts"), Slider("position.sideMargin", 0, 300, 2, "px", false)),
                Row(Loc.Tr("Bildschirm"), Loc.Tr("0 = Hauptbildschirm"), Slider("position.monitor", 0, 4, 1, null, false)));
            Section("position", "Verschieben",
                Row(Loc.Tr("Pille verschieben"), Loc.Tr("klicken, halten und ziehen"), Choice("position.dragMode", () => LT("reset|Zurück beim Öffnen", "keep|Bleibt, wo sie ist", "off|Nicht verschiebbar"), 190)));

            // ===== Tabs =====
            Section("tabs", "Tabs",
                Note(Loc.Tr("Leertaste schaltet, Strg+↑/↓ verschiebt. Ausgeschaltete Tabs werden gar nicht geladen.")),
                OrderList("tabs", TabItems(), 1, true));
            Section("tabs", "Steuerung",
                Note(Loc.Tr("Kacheln an/aus und Reihenfolge – je drei pro Reihe.")),
                OrderList("controls.tiles", TileItems(), 0, true),
                Row(Loc.Tr("Lautstärke-Regler"), null, Toggle("controls.showVolume", null)),
                Row(Loc.Tr("Medien-Karte"), Loc.Tr("Cover, Titel, Spulen"), Toggle("controls.showMedia", null)));

            var presetsTb = new TextBox { Text = string.Join(", ", GL("today.timerPresets")) };
            Action commitPresets = () =>
            {
                var nums = presetsTb.Text.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => { int n; return int.TryParse(x, out n) ? n : -1; }).Where(n => n > 0 && n <= 1440).Take(6).ToList();
                S("today.timerPresets", nums.Cast<object>().ToList());
                presetsTb.Text = string.Join(", ", nums);
            };
            presetsTb.LostKeyboardFocus += (s, e) => commitPresets();
            presetsTb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { commitPresets(); e.Handled = true; } };
            Section("tabs", "Heute",
                Row(Loc.Tr("Wetter-Karte"), null, Toggle("today.showWeather", null)),
                Row(Loc.Tr("Timer-Karte"), null, Toggle("today.showTimer", null)),
                Row(Loc.Tr("To-dos"), null, Toggle("today.showTodos", null)),
                Row(Loc.Tr("Ort fürs Wetter"), Loc.Tr("leer = kein Wetter"), TextField("today.weatherCity", Loc.Tr("z. B. Hamburg"), 180)),
                Row(Loc.Tr("Einheit"), null, Seg("today.weatherUnit", new[] { "C:°C", "F:°F" }, null)),
                Row(Loc.Tr("Timer-Vorgaben"), Loc.Tr("Minuten, mit Komma getrennt"), Field(presetsTb, 180)));
            Section("tabs", "Apps",
                Tagged(LinksList(), Loc.Tr("Schnellzugriffe") + " Links"),
                Row(Loc.Tr("Favoriten anzeigen"), null, Toggle("apps.showFavorites", null)),
                Row(Loc.Tr("Zuletzt benutzt"), Loc.Tr("Anzahl, 0 = aus"), Slider("apps.recentCount", 0, 12, 1, null, false)),
                Row(Loc.Tr("Kompakte Liste"), Loc.Tr("niedrigere Zeilen, kleinere Icons"), Toggle("apps.compact", null)),
                Row(Loc.Tr("Art anzeigen"), Loc.Tr("z. B. „Anwendung“ rechts in der Zeile"), Toggle("apps.showKind", null)),
                Row(Loc.Tr("Websuche"), null, Choice("apps.webSearch", () =>
                {
                    var l = L("https://www.google.com/search?q=|Google", "https://duckduckgo.com/?q=|DuckDuckGo", "https://www.bing.com/search?q=|Bing",
                              "https://www.ecosia.org/search?q=|Ecosia", "https://www.startpage.com/do/search?q=|Startpage", "https://search.brave.com/search?q=|Brave");
                    l.Add(new[] { "", Loc.Tr("Aus") });
                    string cur = GS("apps.webSearch", "");
                    if (!l.Any(x => x[0] == cur)) l.Add(new[] { cur, Loc.Tr("Eigene") });
                    return l;
                }, 190)),
                Row(Loc.Tr("Deinstallationsprogramme ausblenden"), null, Toggle("apps.hideUninstallers", null)));
            Section("tabs", "Taskbar",
                Row(Loc.Tr("Nach App gruppieren"), Loc.Tr("aus = einfache Liste aller Fenster"), Toggle("taskbar.group", null)));
            Section("tabs", "Zwischenablage",
                Row(Loc.Tr("Verlauf aktiv"), null, Toggle("clipboard.enabled", null)),
                Row(Loc.Tr("Einträge merken"), null, Slider("clipboard.maxItems", 10, 200, 5, null, false)),
                Row(Loc.Tr("Nach Neustart behalten"), Loc.Tr("nur Text, Bilder nie"), Toggle("clipboard.persist", null)),
                Row(Loc.Tr("Filter beim Öffnen"), null, Seg("clipboard.defaultFilter", new[] { "all:" + Loc.Tr("Alle"), "text:Text", "image:" + Loc.Tr("Bilder"), "link:Links" }, null)));
            Section("tabs", "Power",
                Note(Loc.Tr("Knöpfe an/aus und Reihenfolge.")),
                OrderList("power.buttons", PowerItems(), 1, true),
                Row(Loc.Tr("Herunterfahren bestätigen"), Loc.Tr("zweimal drücken"), Toggle("power.confirm", null)));

            // ===== Info =====
            var ver = U.T("v" + NookApp.Version + (Program.IsTest ? "  ·  TEST" : ""), 13, Th.Text, FontWeights.SemiBold);
            Section("info", "Nook",
                Row(Loc.Tr("Version"), Program.IsTest ? Loc.Tr("Testversion – läuft neben der stabilen Version, eigene Einstellungen") : Loc.Tr("Stabile Version"), ver),
                Row(Loc.Tr("Einstellungsdatei"), Loc.Tr("config.json – zum Sichern oder Teilen"), Button(U.T(Loc.Tr("Ordner öffnen"), 13, Th.Text, FontWeights.SemiBold), false, () => app.OpenConfigFolder())));
            Section("info", "Tastatur",
                Note(Loc.Tr("Strg+S speichert  ·  Esc bricht ab  ·  Strg+F sucht  ·  Strg+Bild↑/↓ wechselt den Bereich  ·  Tab springt zum nächsten Feld  ·  Leertaste schaltet  ·  ← → ändert Werte")),
                Note(Loc.Tr("Im Panel: F1 zeigt alle Tastenkürzel.")));

            ApplyFilter();
        }

        // Reihenfolge-Editor für Listen in der Config (Tabs, Kacheln, Power-Knöpfe, Pillen-Inhalt)
        // canToggle: Einträge lassen sich an- und ausschalten; minOn: so viele müssen mindestens an bleiben
        UIElement OrderList(string path, string[][] items, int minOn, bool canToggle)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var known = items.Select(x => x[0]).ToList();
            Func<List<string>> cur = () =>
            {
                var l = GL(path).Where(known.Contains).Distinct().ToList();
                if (!canToggle) foreach (var k in known) if (!l.Contains(k)) l.Add(k);
                return l;
            };
            var on0 = cur();
            var order = on0.Concat(known.Where(x => !on0.Contains(x))).ToList();
            foreach (var id0 in order)
            {
                string id = id0;
                var it = items.First(x => x[0] == id);
                bool on = on0.Contains(id);
                int idx = on0.IndexOf(id);
                var g = U.G(null, new[] { U.Px(30), U.Px(26), U.Star(1), U.Auto, U.Auto });
                UIElement lead;
                if (canToggle)
                    lead = new Border
                    {
                        Width = 18, Height = 18, CornerRadius = new CornerRadius(6), Background = on ? Th.Accent : Brushes.Transparent,
                        BorderBrush = on ? Th.Accent : Th.Muted, BorderThickness = new Thickness(1.5), Child = on ? U.I("", 10, Th.AccentText) : null,
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                else lead = U.T((idx + 1).ToString(), 12.5, Th.Muted, FontWeights.Bold);
                U.Put(g, lead, 0, 0);
                U.Put(g, U.I(it[2], 13, on ? Th.Text : Th.Muted), 0, 1);
                U.Put(g, U.T(it[1], 13.5, on ? Th.Text : Th.Muted, FontWeights.SemiBold), 0, 2);
                var up = U.IconBtn("", 26, 10, Brushes.Transparent, Th.Hover, Th.Muted);
                var dn = U.IconBtn("", 26, 10, Brushes.Transparent, Th.Hover, Th.Muted);
                up.Opacity = on && idx > 0 ? 1 : 0.25; dn.Opacity = on && idx < on0.Count - 1 ? 1 : 0.25;
                U.Put(g, up, 0, 3); U.Put(g, dn, 0, 4);
                var row = new FocusBox { Child = g, Background = Th.Surface2, CornerRadius = new CornerRadius(11), Padding = new Thickness(10, 5, 6, 5), Margin = new Thickness(0, 0, 0, 6), Cursor = canToggle ? Cursors.Hand : Cursors.Arrow };
                Action toggle = () =>
                {
                    if (!canToggle) return;
                    var l = cur();
                    if (l.Contains(id)) { if (l.Count <= minOn) return; l.Remove(id); } else l.Add(id);
                    S(path, l.Cast<object>().ToList());
                    RebuildKeepFocus(path + ":" + id);
                };
                Action<int> move = d =>
                {
                    var l = cur();
                    int i = l.IndexOf(id), j = i + d;
                    if (i < 0 || j < 0 || j >= l.Count) return;
                    l[i] = l[j]; l[j] = id;
                    S(path, l.Cast<object>().ToList());
                    RebuildKeepFocus(path + ":" + id);
                };
                row.MouseLeftButtonUp += (s, e) => { if (!up.IsMouseOver && !dn.IsMouseOver) toggle(); e.Handled = true; };
                up.Click += () => move(-1);
                dn.Click += () => move(1);
                row.KeyDown += (s, e) =>
                {
                    bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                    if (e.Key == Key.Space || e.Key == Key.Enter) { toggle(); e.Handled = true; }
                    else if (ctrl && e.Key == Key.Up) { move(-1); e.Handled = true; }
                    else if (ctrl && e.Key == Key.Down) { move(1); e.Handled = true; }
                };
                row.Tag = path + ":" + id;
                sp.Children.Add(row);
            }
            return Tagged(sp, Loc.Tr("Reihenfolge") + " " + string.Join(" ", items.Select(x => x[1])));
        }

        void RebuildKeepScroll()
        {
            double y = formScroll.VerticalOffset;
            BuildForm();
            formScroll.UpdateLayout(); formScroll.ScrollToVerticalOffset(y);
        }

        void RebuildKeepFocus(string tag)
        {
            RebuildKeepScroll();
            Dispatcher.BeginInvoke(new Action(() =>
            {
                var el = FindTag(form, tag) as UIElement;
                if (el != null) { el.Focus(); Keyboard.Focus(el); }
            }), DispatcherPriority.Input);
        }

        ScrollViewer FindScroll()
        {
            DependencyObject d = form;
            while (d != null && !(d is ScrollViewer)) d = VisualTreeHelper.GetParent(d);
            return d as ScrollViewer;
        }

        static DependencyObject FindTag(DependencyObject d, string tag)
        {
            int n = VisualTreeHelper.GetChildrenCount(d);
            for (int i = 0; i < n; i++)
            {
                var c = VisualTreeHelper.GetChild(d, i);
                var fe = c as FrameworkElement;
                if (fe != null && tag.Equals(fe.Tag)) return c;
                var r = FindTag(c, tag);
                if (r != null) return r;
            }
            return null;
        }

        UIElement LinksList()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 6) };
            var hint = U.T(Loc.Tr("Schnellzugriffe erscheinen als Chips über der App-Liste – Webadresse, Programm oder Ordner."), 11.5, Th.Muted);
            hint.TextWrapping = TextWrapping.Wrap; hint.TextTrimming = TextTrimming.None; hint.Margin = new Thickness(0, 0, 0, 8);
            sp.Children.Add(hint);
            var links = Json.Seq(G("apps.quickLinks")).OfType<Dictionary<string, object>>().ToList();
            S2("apps.quickLinks", links.Cast<object>().ToList());
            Action save = () => S("apps.quickLinks", links.Cast<object>().ToList());
            foreach (var l0 in links)
            {
                var l = l0;
                var g = U.G(null, new[] { U.Px(120), U.Px(6), U.Star(1), U.Px(4), U.Auto });
                object nv, tv;
                var name = new TextBox { Text = l.TryGetValue("name", out nv) ? Convert.ToString(nv) : "" };
                var target = new TextBox { Text = l.TryGetValue("target", out tv) ? Convert.ToString(tv) : "" };
                name.TextChanged += (s, e) => { l["name"] = name.Text; save(); };
                target.TextChanged += (s, e) => { l["target"] = target.Text; save(); };
                U.Put(g, Field(name, 0), 0, 0);
                U.Put(g, Field(target, 0), 0, 2);
                var del = U.IconBtn("", 28, 10, Brushes.Transparent, Th.Hover, Th.Muted);
                del.Click += () => { links.Remove(l); save(); RebuildKeepScroll(); };
                U.Put(g, del, 0, 4);
                g.Margin = new Thickness(0, 0, 0, 6);
                sp.Children.Add(g);
            }
            var add = Button(U.T(Loc.Tr("+ Schnellzugriff"), 12.5, Th.Text, FontWeights.SemiBold), false, () =>
            {
                links.Add(new Dictionary<string, object> { { "name", "" }, { "target", "" } });
                save(); RebuildKeepScroll();
            });
            add.HorizontalAlignment = HorizontalAlignment.Left; add.Margin = new Thickness(0, 2, 0, 0); add.Background = Th.Surface2;
            sp.Children.Add(add);
            return sp;
        }

        // ---------------- Vorschau ----------------
        void BuildPvSeg()
        {
            pvSeg.Children.Clear();
            foreach (var m in new[] { new[] { "pill", Loc.Tr("Pille") }, new[] { "panel", "Panel" } })
            {
                string id = m[0];
                bool on = pvMode == id;
                var b = new Btn(U.T(m[1], 12.5, on ? Th.Accent : Th.Muted, FontWeights.SemiBold), on ? Th.AccentSoft : Th.Surface2, on ? Th.AccentSoft : Th.Hover, 9, new Thickness(12, 5, 12, 6));
                b.Margin = new Thickness(4, 0, 0, 0);
                b.Click += () => { pvMode = id; BuildPvSeg(); RebuildPreview(); };
                pvSeg.Children.Add(b);
            }
        }

        void RebuildPreview()
        {
            if (draft == null) return;
            var cfg = new Cfg { Root = draft };
            Th.Apply(cfg);
            try
            {
                logoImg.Source = NookApp.Logo(30, cfg);
                pvHost.Child = BuildStage(cfg);
            }
            catch (Exception ex) { NookApp.Log(ex); }
            finally { Th.Apply(app.Cfg); }
        }

        static CornerRadius Corners(Cfg c, double r)
        {
            if (c.D("position.offset", 8) > 0.5) return new CornerRadius(r);
            return c.S("position.edge", "top") == "bottom" ? new CornerRadius(r, r, 0, 0) : new CornerRadius(0, 0, r, r);
        }

        UIElement BuildStage(Cfg c)
        {
            double pw = c.D("panel.width", 780), ph = c.D("panel.height", 470);
            bool bottom = c.S("position.edge", "top") == "bottom";
            string align = c.S("position.align", "center");
            double off = c.D("position.offset", 8), side = c.D("position.sideMargin", 16);
            double stageW = Math.Max(pw + 2 * side + 160, 1100);
            double stageH = pvMode == "panel" ? ph + off + 90 : 260;
            var bg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 1) };
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(36, 46, 62), 0));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(70, 76, 86), 0.55));
            bg.GradientStops.Add(new GradientStop(Color.FromRgb(176, 138, 72), 1));
            var canvas = new Grid { Width = stageW, Height = stageH, Background = bg, ClipToBounds = true };
            FrameworkElement el = pvMode == "panel" ? MockPanel(c, pw, ph) : MockPill(c);
            el.HorizontalAlignment = align == "left" ? HorizontalAlignment.Left : align == "right" ? HorizontalAlignment.Right : HorizontalAlignment.Center;
            el.VerticalAlignment = bottom ? VerticalAlignment.Bottom : VerticalAlignment.Top;
            el.Margin = new Thickness(side, bottom ? 0 : off, side, bottom ? off : 0);
            canvas.Children.Add(el);
            var vb = new Viewbox { Child = canvas, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, VerticalAlignment = VerticalAlignment.Top };
            return vb;
        }

        FrameworkElement MockPill(Cfg c)
        {
            double h = Math.Max(22, c.D("pill.height", 34)), fs = c.D("pill.fontSize", 13);
            string mode = c.S("pill.mode", "always");
            bool bottom = c.S("position.edge", "top") == "bottom";
            var now = DateTime.Now;
            bool sec = c.B("pill.clockSeconds", false);
            string df = c.S("pill.dateFormat", "short");
            var order = c.L("pill.order").Select(x => Convert.ToString(x)).ToList();
            foreach (var id in new[] { "mic", "clock", "date", "timer", "media", "windows" }) if (!order.Contains(id)) order.Add(id);

            // Teile in der eingestellten Reihenfolge: (art, element) – art "win" für die App-Icons
            var parts = new List<KeyValuePair<string, FrameworkElement>>();
            foreach (var id in order)
            {
                FrameworkElement el = null;
                if (id == "clock" && c.B("pill.showClock", true))
                    el = U.T(now.ToString(c.B("pill.clock24h", true) ? (sec ? "HH:mm:ss" : "HH:mm") : (sec ? "h:mm:ss tt" : "h:mm tt"), Th.Cul), fs, Th.Text, FontWeights.SemiBold);
                else if (id == "date" && c.B("pill.showDate", false))
                    el = U.T(now.ToString(df == "long" ? (Loc.En ? "dddd, MMMM d" : "dddd, d. MMMM") : df == "numeric" ? (Loc.En ? "MM/dd" : "dd.MM.") : Loc.Tr("ddd d. MMM"), Th.Cul), fs, Th.Muted);
                else if (id == "media" && c.B("pill.showMedia", true))
                {
                    var m = new StackPanel { Orientation = Orientation.Horizontal };
                    m.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = Th.Accent, Margin = new Thickness(0, 1, 7, 0) });
                    var mt = U.T("lofi beats to study and relax  ·  lofi radio", fs, Th.Text);
                    mt.MaxWidth = Math.Max(80, c.D("pill.mediaWidth", 240));
                    m.Children.Add(mt);
                    el = m;
                }
                else if (id == "windows" && c.B("pill.showWindows", true))
                {
                    var wsp = new StackPanel { Orientation = Orientation.Horizontal };
                    var cols = new[] { Color.FromRgb(74, 138, 244), Color.FromRgb(29, 185, 84), Color.FromRgb(59, 143, 214), Color.FromRgb(232, 178, 58) };
                    var letters = new[] { "C", "S", "V", "E" };
                    int n = Math.Min(4, Math.Max(1, (int)c.D("pill.maxWindowIcons", 12)));
                    double box = h - 6, ic = Math.Round(h * 0.56);
                    double icfg = c.D("pill.iconSize", 0);
                    if (icfg > 0) ic = Math.Max(12, Math.Min(box - 2, icfg));
                    for (int i = 0; i < n; i++)
                    {
                        var cell = new Grid { Width = box, Height = box };
                        var t = U.T(letters[i], ic * 0.5, Brushes.White, FontWeights.Bold); t.HorizontalAlignment = HorizontalAlignment.Center;
                        cell.Children.Add(new Border { Width = ic, Height = ic, CornerRadius = new CornerRadius(ic / 4), Background = Th.B(cols[i]), Child = t });
                        cell.Children.Add(new Border { Width = i == 0 ? 10 : 4, Height = 3, CornerRadius = new CornerRadius(1.5), Background = i == 0 ? Th.Accent : Th.Muted, VerticalAlignment = VerticalAlignment.Bottom, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 1) });
                        wsp.Children.Add(new Border { Child = cell, CornerRadius = new CornerRadius(box / 3), Background = i == 0 ? Th.Hover : Brushes.Transparent, Margin = new Thickness(1, 0, 1, 0) });
                    }
                    parts.Add(new KeyValuePair<string, FrameworkElement>("win", wsp));
                    continue;
                }
                if (el != null) parts.Add(new KeyValuePair<string, FrameworkElement>(id, el));
            }
            var sp = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            for (int i = 0; i < parts.Count; i++)
            {
                var el = parts[i].Value;
                el.VerticalAlignment = VerticalAlignment.Center;
                if (i > 0)
                {
                    bool winNow = parts[i].Key == "win", winPrev = parts[i - 1].Key == "win";
                    if (winNow || winPrev)
                        sp.Children.Add(new Border { Width = 1, Height = h * 0.45, Background = Th.Faint, Margin = winNow ? new Thickness(12, 0, 8, 0) : new Thickness(8, 0, 12, 0) });
                    else el.Margin = new Thickness(parts[i].Key == "date" && parts[i - 1].Key == "clock" ? 8 : 12, 0, 0, 0);
                }
                sp.Children.Add(el);
            }
            double r = h / 2 * Math.Max(0, Math.Min(100, c.D("pill.roundness", 100))) / 100.0;
            var pill = new Border { Height = h, MinWidth = h * 2, Background = Th.PillBg, CornerRadius = Corners(c, r), Padding = new Thickness(h * 0.5, 0, h * 0.5, 0), Child = sp };
            if (c.B("pill.border", false)) { pill.BorderBrush = Th.B(Th.BorderC); pill.BorderThickness = new Thickness(Math.Max(1, Th.BorderW)); }
            if (c.B("panel.shadow", true)) pill.Effect = new DropShadowEffect { BlurRadius = 14, ShadowDepth = 2, Direction = 270, Opacity = Th.ShadowOpacity * 0.8, Color = Colors.Black };

            var caption = new Func<string, TextBlock>(t =>
            {
                var tb = U.T(t, 14, Brushes.White, FontWeights.SemiBold);
                tb.HorizontalAlignment = HorizontalAlignment.Center; tb.Margin = new Thickness(0, 12, 0, 12); tb.Opacity = 0.85;
                return tb;
            });
            if (mode == "hidden")
            {
                var col = new StackPanel();
                col.Children.Add(caption(Loc.Tr("Pille ausgeblendet – Nook öffnet sich mit der Win-Taste")));
                return col;
            }
            if (mode == "lip")
            {
                var col = new StackPanel();
                double lw = Math.Max(24, Math.Min(400, c.D("pill.lipWidth", 72))), lt = Math.Max(2, Math.Min(12, c.D("pill.lipThickness", 4)));
                var lipBar = new Border { Width = lw, Height = lt, Background = Th.B(Th.A(Th.AccentC, 190)), CornerRadius = bottom ? new CornerRadius(3, 3, 0, 0) : new CornerRadius(0, 0, 3, 3), Margin = new Thickness(0, bottom ? 12 : 0, 0, bottom ? 0 : 12) };
                pill.HorizontalAlignment = HorizontalAlignment.Center;
                if (!bottom) col.Children.Add(lipBar);
                col.Children.Add(pill);
                col.Children.Add(caption(Loc.Tr("Erscheint, wenn die Maus den Strich am Rand berührt")));
                if (bottom) col.Children.Add(lipBar);
                return col;
            }
            return pill;
        }

        FrameworkElement MockPanel(Cfg c, double pw, double ph)
        {
            var tabs = Json.Seq(c.Get("tabs")).Select(x => Convert.ToString(x)).ToList();
            if (!tabs.Contains(pvTab)) pvTab = tabs.Contains("controls") ? "controls" : (tabs.FirstOrDefault() ?? "controls");
            var g = U.G(new[] { U.Auto, U.Star(1) }, null);
            bool tabsLeft = c.S("panel.tabAlign", "center") == "left";
            var bar = tabsLeft ? U.G(null, new[] { U.Auto, U.Star(1), U.Auto }) : U.G(null, new[] { U.Star(1), U.Auto, U.Star(1) });
            bar.Margin = new Thickness(22, 16, 18, 14);
            var tsp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = tabsLeft ? HorizontalAlignment.Left : HorizontalAlignment.Center };
            if (tabsLeft) tsp.Margin = new Thickness(14, 0, 0, 0);
            foreach (var t in tabs)
            {
                string id = t;
                var b = TabLook.Make(id, TabName(id), c);
                TabLook.SetOn(b, id == pvTab, c);
                b.Click += () => { pvTab = id; RebuildPreview(); };
                tsp.Children.Add(b);
            }
            U.Put(bar, tsp, 0, 1);
            if (c.B("panel.showClock", true))
            {
                var now = DateTime.Now;
                var cs = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                cs.Children.Add(U.T(now.ToString(c.B("pill.clock24h", true) ? "HH:mm" : "h:mm tt", Th.Cul), 15, Th.Text, FontWeights.SemiBold));
                var dt = U.T(now.ToString(Loc.Tr("ddd d. MMM"), Th.Cul), 12.5, Th.Muted, FontWeights.SemiBold); dt.Margin = new Thickness(8, 1, 0, 0);
                cs.Children.Add(dt);
                U.Put(bar, cs, 0, 0);
            }
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            if (c.B("panel.showHelpButton", true)) right.Children.Add(U.IconBtn("", 32, 14, Brushes.Transparent, Th.Surface, Th.Muted));
            if (c.B("panel.showMenuButton", true)) right.Children.Add(U.IconBtn("", 32, 14, Brushes.Transparent, Th.Surface, Th.Muted));
            U.Put(bar, right, 0, 2);
            U.Put(g, bar, 0, 0);
            var body = new Border { Margin = new Thickness(18, 0, 18, 18), IsHitTestVisible = false, Child = MockBody(pvTab, c) };
            U.Put(g, body, 1, 0);
            bool auto = pvTab == "power";
            var panel = new Border
            {
                Width = pw, Background = Th.PanelBg, BorderBrush = Th.B(Th.BorderC), BorderThickness = new Thickness(Th.BorderW),
                CornerRadius = Corners(c, c.D("panel.radius", 24)), Child = g, ClipToBounds = false
            };
            if (!auto) panel.Height = ph;
            if (c.B("panel.shadow", true)) panel.Effect = new DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = Th.ShadowOpacity, Color = Colors.Black };
            return panel;
        }

        static Border Card(UIElement child, Thickness pad, double radiusAdd)
        {
            var b = new Border { Padding = pad, Child = child };
            Th.StyleCard(b, radiusAdd);
            return b;
        }

        UIElement MockBody(string tab, Cfg c)
        {
            if (tab == "controls")
            {
                var all = TileItems();
                var ids = c.L("controls.tiles").Select(x => Convert.ToString(x)).Where(x => all.Any(a => a[0] == x)).Distinct().ToList();
                var subs = new Dictionary<string, string[]>
                {
                    { "wifi", new[] { "1", Loc.Tr("Zuhause 5G") } }, { "bluetooth", new[] { "0", Loc.Tr("Einstellungen") } }, { "audio", new[] { "1", Loc.Tr("Lautsprecher") } },
                    { "night", new[] { "0", Loc.Tr("Einstellungen") } }, { "dark", new[] { "1", Loc.Tr("An") } }, { "mic", new[] { "0", Loc.Tr("An") } }
                };
                var rowsDef = new List<GridLength>();
                int tileRows = (ids.Count + 2) / 3;
                for (int i = 0; i < tileRows; i++) { rowsDef.Add(U.Px(62)); rowsDef.Add(U.Px(10)); }
                bool showVol = c.B("controls.showVolume", true), showMedia = c.B("controls.showMedia", true);
                int volRow = rowsDef.Count;
                if (showVol) { rowsDef.Add(U.Px(48)); rowsDef.Add(U.Px(12)); }
                int mediaRow = rowsDef.Count;
                rowsDef.Add(U.Star(1));
                var g = U.G(rowsDef.ToArray(), null);
                for (int r = 0; r < tileRows; r++)
                {
                    var rg = U.G(null, new[] { U.Star(1), U.Px(10), U.Star(1), U.Px(10), U.Star(1) });
                    for (int ci = 0; ci < 3; ci++)
                    {
                        int i = r * 3 + ci;
                        if (i >= ids.Count) break;
                        var def = all.First(a => a[0] == ids[i]);
                        var tile = new Tile(def[2], def[1], ids[i] != "dark" && ids[i] != "mic");
                        tile.Set(subs[ids[i]][0] == "1", subs[ids[i]][1]);
                        U.Put(rg, tile, 0, ci * 2);
                    }
                    U.Put(g, rg, r * 2, 0);
                }
                if (showVol) U.Put(g, new FillSlider(48) { Value = 0.62 }, volRow, 0);
                if (showMedia)
                {
                    var mg = U.G(null, new[] { U.Px(72), U.Px(16), U.Star(1), U.Auto });
                    var cover = new Border { Width = 72, Height = 72, CornerRadius = new CornerRadius(12), Background = new LinearGradientBrush(Th.Mix(Th.AccentC, Colors.Black, 0.35), Th.Mix(Th.AccentC, Color.FromRgb(120, 60, 140), 0.6), 45) };
                    U.Put(mg, cover, 0, 0);
                    var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                    tx.Children.Add(U.T("lofi beats to study and relax", 20, Th.Text, FontWeights.SemiBold));
                    tx.Children.Add(U.T("lofi radio", 13, Th.Muted));
                    U.Put(mg, tx, 0, 2);
                    var ctl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                    ctl.Children.Add(U.IconBtn("", 40, 13, Th.Surface2, Th.Surface2, Th.Text));
                    var play = U.IconBtn("", 54, 18, Th.B(Th.Mix(Th.TextC, Th.AccentC, 0.12)), Th.Text, Th.B(Color.FromRgb(20, 20, 20)));
                    play.Margin = new Thickness(12, 0, 12, 0);
                    ctl.Children.Add(play);
                    ctl.Children.Add(U.IconBtn("", 40, 13, Th.Surface2, Th.Surface2, Th.Text));
                    U.Put(mg, ctl, 0, 3);
                    var card = Card(mg, new Thickness(18, 14, 18, 14), 2);
                    card.VerticalAlignment = VerticalAlignment.Top;
                    U.Put(g, card, mediaRow, 0);
                }
                return g;
            }
            if (tab == "apps")
            {
                bool compact = c.B("apps.compact", false), kind = c.B("apps.showKind", true);
                double rh = compact ? 36 : 46, isz = compact ? 22 : 28;
                var sp = new StackPanel();
                sp.Children.Add(new SearchBox(Loc.Tr("Apps durchsuchen oder rechnen …"), "Win", 16, true) { Height = 54 });
                Func<string, string, bool, bool, UIElement> row = (n, k, star, sel) =>
                {
                    var rg = U.G(null, new[] { U.Px(isz + 4), U.Px(12), U.Star(1), U.Auto, U.Px(30) });
                    U.Put(rg, U.Letter(n, isz), 0, 0);
                    U.Put(rg, U.T(n, compact ? 13.5 : 14.5, Th.Text, FontWeights.SemiBold), 0, 2);
                    if (kind) U.Put(rg, U.T(Loc.Tr(k), 12, Th.Muted, FontWeights.SemiBold), 0, 3);
                    U.Put(rg, U.I(star ? "" : "", 13, star ? Th.Accent : Th.Faint), 0, 4);
                    return new Border { Height = rh, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 0, 10, 0), Background = sel ? Th.AccentSoft : Brushes.Transparent, Child = rg, Margin = new Thickness(0, 0, 0, 2) };
                };
                bool first = true;
                if (c.B("apps.showFavorites", true))
                {
                    sp.Children.Add(U.Section(Loc.Tr("Favoriten")));
                    sp.Children.Add(row("Spotify", "Anwendung", true, first));
                    sp.Children.Add(row("Visual Studio Code", "Anwendung", true, false));
                    first = false;
                }
                int rc = (int)c.D("apps.recentCount", 5);
                if (rc > 0)
                {
                    sp.Children.Add(U.Section(Loc.Tr("Zuletzt")));
                    var names = new[] { "Google Chrome", "Explorer", Loc.Tr("Rechner"), "Discord", "Paint" };
                    for (int i = 0; i < Math.Min(rc, compact ? 5 : 3); i++) { sp.Children.Add(row(names[i], "Anwendung", false, first)); first = false; }
                }
                return sp;
            }
            if (tab == "power")
            {
                var all = PowerItems();
                var ids = c.L("power.buttons").Select(x => Convert.ToString(x)).Where(x => all.Any(a => a[0] == x)).Distinct().ToList();
                if (ids.Count == 0) ids.Add("lock");
                var ug = new UniformGrid { Rows = 1, Columns = ids.Count, Margin = new Thickness(0, 0, 0, 4) };
                foreach (var id in ids)
                {
                    var p = all.First(a => a[0] == id);
                    var s = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
                    s.Children.Add(U.I(p[2], 24, Th.Text));
                    var l = U.T(p[1], 13.5, Th.Text, FontWeights.SemiBold); l.Margin = new Thickness(0, 12, 0, 0); l.HorizontalAlignment = HorizontalAlignment.Center;
                    s.Children.Add(l);
                    var tb = Card(s, new Thickness(0), 2);
                    tb.Height = 128; tb.Margin = new Thickness(5, 0, 5, 0);
                    ug.Children.Add(tb);
                }
                return ug;
            }
            var desc = tab == "today" ? Loc.Tr("Uhr, Woche, Timer, To-dos und Wetter") : tab == "taskbar" ? Loc.Tr("Offene Fenster, nach App sortiert") : Loc.Tr("Verlauf mit Suche, Filtern und Vorschau");
            var e = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            var t0 = U.T(TabName(tab), 17, Th.Text, FontWeights.SemiBold); t0.HorizontalAlignment = HorizontalAlignment.Center;
            var t1b = U.T(desc, 13, Th.Muted); t1b.HorizontalAlignment = HorizontalAlignment.Center; t1b.Margin = new Thickness(0, 6, 0, 0);
            var t2b = U.T(Loc.Tr("(in der Vorschau vereinfacht)"), 12, Th.Muted); t2b.HorizontalAlignment = HorizontalAlignment.Center; t2b.Margin = new Thickness(0, 4, 0, 0);
            e.Children.Add(t0); e.Children.Add(t1b); e.Children.Add(t2b);
            return Card(e, new Thickness(0), 0);
        }
    }
}

namespace Nook
{
    // -----------------------------------------------------------------
    //  Audio-Fenster: Ausgabegerät wählen und Lautstärke pro App
    //  Tastatur: ↑ ↓ wählen · Enter Gerät setzen / App stumm · ← → Lautstärke · Alt+M stumm · Esc
    // -----------------------------------------------------------------
    public class AudioFlyout : Window
    {
        public static AudioFlyout Current;

        readonly NookApp app;
        readonly Rect anchor;
        readonly StackPanel sp = new StackPanel { MinWidth = 380 };
        readonly List<Border> rows = new List<Border>();
        readonly List<Action<int>> rowKeys = new List<Action<int>>();   // 0 = Enter, -1/+1 = ← →, 2 = stumm
        readonly List<Action> rowRefresh = new List<Action>();
        int sel = -1;
        bool closing;
        DateTime openedAt;
        OutsideClick outside;
        Action<IntPtr> fgHandler;
        IntPtr hwnd;

        public AudioFlyout(NookApp a, Rect anchorScreen)
        {
            app = a; anchor = anchorScreen;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; Background = Brushes.Transparent;
            Topmost = true; ShowInTaskbar = false; ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.WidthAndHeight;
            Title = "Nook Audio";
            UseLayoutRounding = true; FontFamily = Th.Font;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            Content = new Border
            {
                Background = Th.Bg, CornerRadius = new CornerRadius(18), Padding = new Thickness(10), Margin = new Thickness(16),
                LayoutTransform = new ScaleTransform(Th.K, Th.K),
                Child = new ScrollViewer { Content = sp, VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, MaxHeight = 620, Focusable = false },
                Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 4, Direction = 270, Opacity = 0.45, Color = Colors.Black }
            };
            SourceInitialized += (s, e) => { hwnd = new WindowInteropHelper(this).Handle; N.AddExStyle(hwnd, 0x80); };
            PreviewKeyDown += OnKey;
            outside = new OutsideClick(() => CloseFlyout(), root => root == hwnd);
            fgHandler = hw =>
            {
                if (closing || (DateTime.Now - openedAt).TotalMilliseconds < 600 || hw == IntPtr.Zero) return;
                uint pid; N.GetWindowThreadProcessId(hw, out pid);
                if (pid != (uint)Process.GetCurrentProcess().Id) CloseFlyout();
            };
            Build();
        }

        public static void Toggle(NookApp a, Rect anchorScreen)
        {
            if (Current != null) { Current.CloseFlyout(); return; }
            if (MenuWindow.Current != null) MenuWindow.Current.CloseMenu();
            var f = new AudioFlyout(a, anchorScreen);
            Current = f;
            f.Open();
        }

        void Open()
        {
            Left = -10000; Top = -10000;
            Show();
            UpdateLayout();
            var mon = app.MonitorDip();
            double k = Th.K;
            double x = Math.Max(mon.Left + 4, Math.Min(anchor.X - 16 * k, mon.Right - ActualWidth - 4));
            bool below = anchor.Y + anchor.Height / 2 < mon.Top + mon.Height / 2;
            double y = below ? anchor.Bottom - 8 * k : anchor.Top - ActualHeight + 8 * k;
            Left = x; Top = Math.Max(mon.Top, Math.Min(y, mon.Bottom - ActualHeight));
            N.SetWindowPos(hwnd, N.HWND_TOPMOST, 0, 0, 0, 0, 0x1 | 0x2 | 0x10);
            Activate();
            N.ForceForeground(hwnd);
            Focus();
            openedAt = DateTime.Now;
            outside.Start();
            NookApp.ForegroundChanged += fgHandler;
            if (rows.Count > 0) SetSel(0);
        }

        public void CloseFlyout()
        {
            if (closing) return;
            closing = true;
            outside.Stop();
            NookApp.ForegroundChanged -= fgHandler;
            if (Current == this) Current = null;
            Close();
            if (app.Panel != null && app.Panel.IsOpen) app.Dispatcher.BeginInvoke(new Action(app.Panel.Refocus), DispatcherPriority.Input);
        }

        // ---------------- Aufbau ----------------
        void Build()
        {
            sp.Children.Clear(); rows.Clear(); rowKeys.Clear(); rowRefresh.Clear();
            int keep = sel; sel = -1;

            sp.Children.Add(Head("Ausgabe"));
            var outs = Audio.Outputs();
            if (outs.Count == 0) sp.Children.Add(Note("Kein Gerät"));
            foreach (var d0 in outs)
            {
                var d = d0;
                var g = U.G(null, new[] { U.Px(28), U.Star(1), U.Px(24) });
                U.Put(g, U.I("", 14, d.IsDefault ? Th.Accent : Th.Muted), 0, 0);
                var t = U.T(d.Name, 13.5, d.IsDefault ? Th.Text : Th.Muted, d.IsDefault ? FontWeights.SemiBold : FontWeights.Normal);
                t.MaxWidth = 300;
                U.Put(g, t, 0, 1);
                if (d.IsDefault) U.Put(g, U.I("", 12, Th.Accent), 0, 2);
                Action pick = () =>
                {
                    if (d.IsDefault) return;
                    if (Audio.SetDefaultOutput(d.Id)) app.Pill.Flash(Loc.Tr("Ausgabe: ") + d.Name, 2, false);
                    else app.Pill.Flash(Loc.Tr("Gerät konnte nicht gewechselt werden"), 3, false);
                    Build();
                };
                AddRow(g, pick, k => { if (k == 0) pick(); }, null);
            }

            sp.Children.Add(Head("Apps"));
            var apps = Audio.Apps();
            if (apps.Count == 0) sp.Children.Add(Note("Gerade spielt keine App Ton ab."));
            foreach (var a0 in apps)
            {
                var a = a0;
                var g = U.G(null, new[] { U.Px(30), U.Px(140), U.Star(1), U.Px(44), U.Px(32) });
                UIElement icon = null;
                if (!string.IsNullOrEmpty(a.Path)) { var ic = N.ShellIcon(a.Path, 32); if (ic != null) icon = new Image { Source = ic, Width = 20, Height = 20 }; }
                if (icon == null) icon = a.System ? (UIElement)U.I("", 14, Th.Muted) : U.Letter(a.Name, 22);
                U.Put(g, icon, 0, 0);
                var name = U.T(a.Name, 13, Th.Text, FontWeights.SemiBold);
                U.Put(g, name, 0, 1);
                var bar = new SeekBar { Margin = new Thickness(8, 0, 8, 0) };
                var pct = U.T("", 12, Th.Muted, FontWeights.SemiBold); pct.HorizontalAlignment = HorizontalAlignment.Right;
                var mute = U.IconBtn("", 28, 11, Brushes.Transparent, Th.Hover, Th.Muted);
                Action refresh = () =>
                {
                    double v = a.Volume; bool m = a.Muted;
                    bar.Value = v;
                    pct.Text = Math.Round(v * 100) + " %";
                    ((TextBlock)mute.Child).Text = Audio.Glyph(v, m);
                    ((TextBlock)mute.Child).Foreground = m ? Th.Accent : Th.Muted;
                    name.Foreground = m ? Th.Muted : Th.Text;
                };
                bar.Dragged += v => { a.Volume = v; if (a.Muted && v > 0) a.Muted = false; pct.Text = Math.Round(v * 100) + " %"; };
                bar.Seeked += v => { a.Volume = v; refresh(); };
                mute.Click += () => { a.Muted = !a.Muted; refresh(); };
                U.Put(g, bar, 0, 2); U.Put(g, pct, 0, 3); U.Put(g, mute, 0, 4);
                refresh();
                Action<int> key = k =>
                {
                    if (k == 0 || k == 2) a.Muted = !a.Muted;
                    else { a.Volume = Math.Max(0, Math.Min(1, a.Volume + k * 0.05)); if (a.Muted) a.Muted = false; }
                    refresh();
                };
                AddRow(g, null, key, refresh);
            }

            sp.Children.Add(new Border { Height = 1, Background = Th.Faint, Margin = new Thickness(8, 8, 8, 6) });
            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(4, 0, 4, 2) };
            links.Children.Add(Link("Sound-Einstellungen", "ms-settings:sound"));
            links.Children.Add(Link("Lautstärkemixer", "ms-settings:apps-volume"));
            sp.Children.Add(links);

            if (keep >= 0 && rows.Count > 0) SetSel(Math.Min(keep, rows.Count - 1));
        }

        TextBlock Head(string t)
        {
            var h = U.T(Loc.Tr(t).ToUpper(Th.Cul), 10.5, Th.Muted, FontWeights.Bold);
            h.Margin = new Thickness(10, sp.Children.Count == 0 ? 4 : 12, 10, 6);
            return h;
        }

        TextBlock Note(string t)
        {
            var n = U.T(t, 12.5, Th.Muted);
            n.Margin = new Thickness(10, 2, 10, 6);
            return n;
        }

        Btn Link(string text, string uri)
        {
            var b = new Btn(U.T(text, 12.5, Th.Accent, FontWeights.SemiBold), Brushes.Transparent, Th.Hover, 8, new Thickness(8, 5, 8, 6));
            b.Click += () => { CloseFlyout(); app.ClosePanel(); AppIndex.Open(uri); };
            return b;
        }

        void AddRow(Grid content, Action click, Action<int> key, Action refresh)
        {
            int idx = rows.Count;
            var row = new Border { Child = content, CornerRadius = new CornerRadius(10), Padding = new Thickness(8, 7, 6, 7), Background = Brushes.Transparent, Cursor = click != null ? Cursors.Hand : Cursors.Arrow };
            row.MouseEnter += (s, e) => SetSel(idx);
            if (click != null) row.MouseLeftButtonUp += (s, e) => { click(); e.Handled = true; };
            rows.Add(row); rowKeys.Add(key); rowRefresh.Add(refresh);
            sp.Children.Add(row);
        }

        void SetSel(int i)
        {
            if (rows.Count == 0) return;
            if (sel >= 0 && sel < rows.Count) rows[sel].Background = Brushes.Transparent;
            sel = Math.Max(0, Math.Min(rows.Count - 1, i));
            rows[sel].Background = Th.Hover;
            rows[sel].BringIntoView();
        }

        void OnKey(object s, KeyEventArgs e)
        {
            var k = e.Key == Key.System ? e.SystemKey : e.Key;
            bool alt = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
            switch (k)
            {
                case Key.Escape: CloseFlyout(); e.Handled = true; return;
                case Key.Down: SetSel(sel + 1 >= rows.Count ? 0 : sel + 1); e.Handled = true; return;
                case Key.Up: SetSel(sel <= 0 ? rows.Count - 1 : sel - 1); e.Handled = true; return;
                case Key.Enter: case Key.Space: if (sel >= 0) rowKeys[sel](0); e.Handled = true; return;
                case Key.Left: if (sel >= 0) rowKeys[sel](-1); e.Handled = true; return;
                case Key.Right: if (sel >= 0) rowKeys[sel](1); e.Handled = true; return;
                case Key.M: if (alt && sel >= 0) { rowKeys[sel](2); e.Handled = true; } return;
            }
        }
    }
}
