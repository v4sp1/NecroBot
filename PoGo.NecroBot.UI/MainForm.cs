using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Google.Apis.Logging;
using PoGo.NecroBot.CLI;
using PoGo.NecroBot.Logic;
using PoGo.NecroBot.Logic.Event;
using PoGo.NecroBot.Logic.Logging;
using PoGo.NecroBot.Logic.State;
using PoGo.NecroBot.Logic.Utils;

namespace PoGo.NecroBot.UI
{
    public partial class MainForm : Form
    {
        private void LoginWithGoogle(string usercode, string uri)
        {
            Logger.Write($"Goto: {uri} & enter {usercode}", LogLevel.Error);
            rtfLog_LinkClicked(rtfLog, new LinkClickedEventArgs(uri));
            try
            {
                // Must be STAThread in order to use OLE like Clipboard
                var thread = new Thread(() => Clipboard.SetText(usercode));
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();
                Logger.Write("The code has been copied to your clipboard.");
            }
            catch
            {
                // ignored
            }
        }

        public MainForm()
        {
            InitializeComponent();
        }

        private delegate void stats_DirtyCallback(Statistics stats);

        private void stats_Dirty(Statistics stats)
        {
            if (InvokeRequired)
            {
                stats_DirtyCallback d = stats_Dirty;
                Invoke(d, stats);
                return;
            }
            lblUser.Text = stats.PlayerName;
            lblRuntime.Text = stats.GetFormattedRuntime();
            lblLevel.Text = $"Level {stats.CurrentLevel:N0}";
            progress.Value = (int)((double)stats.CurrentLevelExperience / stats.NextLevelExperience * 1000); // out of 1000
            lblXp.Text = $"{stats.CurrentLevelExperience:N0}/{stats.NextLevelExperience:N0} XP";
            lblEta.Text = stats.NextLevelEta == TimeSpan.MaxValue ? "ETA" : stats.NextLevelEta.ToString();
            var runtime = stats.GetRuntime();
            lblXph.Text = (stats.TotalExperience / runtime).ToString("N0");
            lblPph.Text = (stats.TotalPokemons / runtime).ToString("N0");
            lblStardust.Text = stats.TotalStardust.ToString("N0");
            lblTransferred.Text = stats.TotalPokemonsTransfered.ToString("N0");
            lblRecycled.Text = stats.TotalItemsRemoved.ToString("N0");
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            var subPath = "";
            var args = Environment.GetCommandLineArgs();
            if (args.Length > 1)
                subPath = args[1];

            Logger.SetLogger(new RichTextLogger(rtfLog, LogLevel.Info), subPath);

            var settings = GlobalSettings.Load(subPath);

            if (settings == null)
            {
                if (MessageBox.Show(this, "This is your first start and the bot will use the default config!", "NecroBot", MessageBoxButtons.OKCancel, MessageBoxIcon.Information) != DialogResult.OK)
                    Environment.Exit(1);

                settings = GlobalSettings.Load(subPath);
            }

            var session = new Session(new ClientSettings(settings), new LogicSettings(settings));

            /*SimpleSession session = new SimpleSession
            {
                _client = new PokemonGo.RocketAPI.Client(new ClientSettings(settings)),
                _dispatcher = new EventDispatcher(),
                _localizer = new Localizer()
            };

            BotService service = new BotService
            {
                _session = session,
                _loginTask = new Login(session)
            };

            service.Run();
            */

            var machine = new StateMachine();
            var stats = new Statistics();
            stats.DirtyEvent += () => stats_Dirty(stats);

            var aggregator = new StatisticsAggregator(stats);
            var listener = new EventListener();
            var websocket = new WebSocketInterface(settings.WebSocketPort);

            session.EventDispatcher.EventReceived += (evt) => listener.Listen(evt, session);
            session.EventDispatcher.EventReceived += (evt) => aggregator.Listen(evt, session);
            session.EventDispatcher.EventReceived += (evt) => websocket.Listen(evt, session);

            machine.SetFailureState(new LoginState());

            Logger.SetLoggerContext(session);

            session.Navigation.UpdatePositionEvent +=
                (lat, lng) => session.EventDispatcher.Send(new UpdatePositionEvent { Latitude = lat, Longitude = lng });

            session.Client.Login.GoogleDeviceCodeEvent += (usercode, uri) =>
            {
                try
                {
                    Logger.Write(session.Translations.GetTranslation(Logic.Common.TranslationString.OpeningGoogleDevicePage), LogLevel.Warning);
                    Thread.Sleep(5000);
                    Process.Start(uri);
                    var thread = new Thread(() => Clipboard.SetText(usercode)); //Copy device code
                    thread.SetApartmentState(ApartmentState.STA); //Set the thread to STA
                    thread.Start();
                    thread.Join();
                }
                catch (Exception)
                {
                    Logger.Write(session.Translations.GetTranslation(Logic.Common.TranslationString.CouldntCopyToClipboard), LogLevel.Error);
                    Logger.Write(session.Translations.GetTranslation(Logic.Common.TranslationString.CouldntCopyToClipboard2, uri, usercode), LogLevel.Error);
                }
            };

            machine.AsyncStart(new VersionCheckState(), session);

            Console.ReadLine();
        }

        private void rtfLog_LinkClicked(object sender, LinkClickedEventArgs e)
        {
            Process.Start(e.LinkText);
        }
    }
}
