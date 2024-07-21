using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ATL;
using Colossal.IO.AssetDatabase;
using Colossal.IO.AssetDatabase.Internal;
using Colossal.Logging;
using Colossal.UI;
using ExtendedRadio;
using Game;
using Game.Modding;
using Game.SceneFlow;
using HarmonyLib;
using static Colossal.IO.AssetDatabase.AudioAsset;
using static ExtendedRadio.ExtendedRadio;
using static Game.Audio.Radio.Radio;

namespace SimCityRadio {
    public class Mod : IMod {
        public static ILog log = LogManager.GetLogger($"{nameof(SimCityRadio)}.{nameof(Mod)}").SetShowsErrorsInUI(false);
        internal static readonly string s_iconsResourceKey = "simcityradio";
        private static readonly string s_modHarmonyId = $"{nameof(SimCityRadio)}.{nameof(Mod)}";
        private string _pathToCustomRadiosFolder;
        private Harmony _harmony;
        private FileInfo _modFileInfo;


        private Tuple<RadioNetwork, List<RadioChannel>> InflateCustomRadioNetworks() {
            RadioNetwork radioNetwork = CustomRadios.JsonToRadioNetwork(_pathToCustomRadiosFolder + "\\SimCityRadio");
           
            radioNetwork.nameId = radioNetwork.name;
            var channelFolders = Directory.GetDirectories(_pathToCustomRadiosFolder + "\\SimCityRadio");
            List<RadioChannel> channels = new List<RadioChannel>();
            foreach (var channelFolder in channelFolders) {
                RadioChannel channel = CustomRadios.JsonToRadioChannel(channelFolder);
                channel.nameId = channel.name;
                channel.network = "SimCity Radio [DEV FORK]";
                var files = Directory.GetFiles(channelFolder + "\\program\\playlist\\music").ToArray();

                Program program = new Program() {
                    name = channel.name,
                    description = channel.name,
                    endTime = "00:00",
                    startTime = "00:00",
                    loopProgram = true,
                    icon = channel.icon,
                    pairIntroOutro = false
                };

                Segment seg = new Segment();
                seg.type = SegmentType.Playlist;
                seg.clipsCap = files.Length;
                seg.tags= ["type:Music", $"radio channel:{channel.name}"];

                List<AudioAsset> clips = new List<AudioAsset>();
                foreach (var file in files) {
                    AudioAsset audioAsset = new AudioAsset();
                    audioAsset = LoadAudioFile(file, SegmentType.Playlist, radioNetwork.name, channel.name, "Program");
                    clips.Add(audioAsset);
                }

                seg.clips = clips.ToArray();
                program.segments = [seg];
                channel.programs = [program];
                channels.Add(channel);

            }
            var net = new Tuple<RadioNetwork, List<RadioChannel>>(radioNetwork, channels);
            return net;

        }

        public void RadioLoadHandler() {
            try {
                Tuple<RadioNetwork, List<RadioChannel>> net = InflateCustomRadioNetworks();
                CustomRadios.AddRadioNetworkToTheGame(net.Item1);
                foreach (var channel in net.Item2) {
                    CustomRadios.AddRadioChannelToTheGame(channel, _pathToCustomRadiosFolder);
                    Console.WriteLine(string.Format("Added channel:{0}", channel?.name ?? channel?.nameId ?? "NULL - reference"));
                }
            }
            catch (Exception e) {
                log.Error(e);
            }
        }

        public void OnLoad(UpdateSystem updateSystem) {
            if (GameManager.instance.modManager.TryGetExecutableAsset(this, out Colossal.IO.AssetDatabase.ExecutableAsset asset)) {
                log.Info($"Current mod asset at {asset.path}");
            }

            _modFileInfo = new FileInfo(asset.path);
            _pathToCustomRadiosFolder = Path.Combine(_modFileInfo.DirectoryName, "CustomRadios");
            Console.WriteLine(_pathToCustomRadiosFolder);
            _harmony = new(s_modHarmonyId);
            _harmony.PatchAll(typeof(Mod).Assembly);
            _harmony.GetPatchedMethods().ForEach(m => log.Info($"Patched method: {m.Module.Name}:{m.Name}"));
            OnRadioLoaded += new onRadioLoaded(RadioLoadHandler);
            UIManager.defaultUISystem.AddHostLocation(s_iconsResourceKey, _modFileInfo.Directory.FullName, false);
        }

        public void OnDispose() {
            _harmony.UnpatchAll(s_modHarmonyId);
            UIManager.defaultUISystem.RemoveHostLocation(s_iconsResourceKey, _modFileInfo.Directory.FullName);
        }


        public static AudioAsset? LoadAudioFile(string audioFilePath, SegmentType segmentType, string networkName, string radioChannelName, string programName) {

            AssetDataPath assetDataPath = AssetDataPath.Create(audioFilePath, EscapeStrategy.None);
            AudioAsset audioAsset;
            try {
                IAssetData assetData = AssetDatabase.game.AddAsset(assetDataPath);
                if (assetData is AudioAsset audioAsset1) {
                    audioAsset = audioAsset1;
                }
                else {
                    return null;
                }

            }
            catch (Exception e) {
                ExtendedRadioMod.log.Warn(e);
                return null;
            }

            using (Stream writeStream = audioAsset.GetReadStream()) {
                Dictionary<Metatag, string> m_Metatags = [];
                Traverse audioAssetTravers = Traverse.Create(audioAsset);
                Track track = new(audioFilePath, true);
                AddMetaTag(audioAsset, m_Metatags, Metatag.Title, track.Title);
                AddMetaTag(audioAsset, m_Metatags, Metatag.Album, track.Album);
                AddMetaTag(audioAsset, m_Metatags, Metatag.Artist, track.Artist);
                AddMetaTag(audioAsset, m_Metatags, Metatag.Type, track, "TYPE", "Playlist");
                AddMetaTag(audioAsset, m_Metatags, Metatag.Brand, track, "BRAND", null);
                AddMetaTag(audioAsset, m_Metatags, Metatag.RadioStation, track, "RADIO STATION", networkName);
                AddMetaTag(audioAsset, m_Metatags, Metatag.RadioChannel, track, "RADIO CHANNEL", radioChannelName);
                AddMetaTag(audioAsset, m_Metatags, Metatag.PSAType, track, "PSA TYPE", null);
                AddMetaTag(audioAsset, m_Metatags, Metatag.AlertType, track, "ALERT TYPE", null);
                AddMetaTag(audioAsset, m_Metatags, Metatag.NewsType, track, "NEWS TYPE", null);
                AddMetaTag(audioAsset, m_Metatags, Metatag.WeatherType, track, "WEATHER TYPE", null);
                audioAssetTravers.Field("m_Metatags").SetValue(m_Metatags);
            }
            audioAsset.AddTags(CustomRadios.FormatTags(segmentType, programName, radioChannelName, networkName));
            return audioAsset;
        }

        internal static void AddMetaTag(AudioAsset audioAsset, Dictionary<Metatag, string> m_Metatags, Metatag tag, string value) {
            audioAsset.AddTag(value);
            m_Metatags [tag] = value;
        }

        internal static void AddMetaTag(AudioAsset audioAsset, Dictionary<Metatag, string> m_Metatags, Metatag tag, Track trackMeta, string oggTag, string? value = null) {
            string? extendedTag = value ?? GetExtendedTag(trackMeta, oggTag);
            if (!string.IsNullOrEmpty(extendedTag) && extendedTag != null) {
                audioAsset.AddTag(oggTag.ToLower() + ":" + extendedTag);
                AddMetaTag(audioAsset, m_Metatags, tag, extendedTag);
            }
        }

        private static string? GetExtendedTag(Track trackMeta, string tag) => trackMeta.AdditionalFields.TryGetValue(tag, out string? value) ? value : null;
    }
}
