﻿#region header
// ========================================================================
// Copyright (c) 2015 - Julien Caillon (julien.caillon@gmail.com)
// This file (UpdateHandler.cs) is part of 3P.
// 
// 3P is a free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// 3P is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
// 
// You should have received a copy of the GNU General Public License
// along with 3P. If not, see <http://www.gnu.org/licenses/>.
// ========================================================================
#endregion
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using _3PA.Html;
using _3PA.Lib;
using _3PA.Properties;

namespace _3PA.MainFeatures {

    /// <summary>
    /// Handles the update of this software
    /// </summary>
    internal static class UpdateHandler {

        private static string PathUpdateFolder { get { return Path.Combine(Npp.GetConfigDir(), "Update"); } }
        private static string PathDownloadedPlugin { get { return Path.Combine(Npp.GetConfigDir(), "Update", "3P.dll"); } }
        private static string Path7ZipExe { get { return Path.Combine(Npp.GetConfigDir(), "Update", "7z.exe"); } }
        private static string Path7ZipDll { get { return Path.Combine(Npp.GetConfigDir(), "Update", "7z.dll"); } }
        private static string PathUpdaterExe { get { return Path.Combine(Npp.GetConfigDir(), "Update", "3pUpdater.exe"); } }
        private static string PathUpdaterLst { get { return Path.Combine(Npp.GetConfigDir(), "Update", "3pUpdater.lst"); } }
        private static string PathLatestReleaseZip { get { return Path.Combine(Npp.GetConfigDir(), "Update", "latestRelease.zip"); } }
        private static string PathToVersionLog { get { return Path.Combine(Npp.GetConfigDir(), "version.log"); } }

        /// <summary>
        /// Holds the info about the latest release found on the distant update server
        /// </summary>
        public static ReleaseInfo LatestReleaseInfo { get; set; }

        /// <summary>
        /// Gets an object with the latest release info
        /// </summary>
        public static void GetLatestReleaseInfo() {
            GetLatestReleaseInfo(false);
        }

        private static bool _alwaysGetFeedBack;
        private static bool _checkStarted;

        /// <summary>
        /// To call when the user click on an update button
        /// </summary>
        public static void CheckForUpdates() {
            if (!Utils.IsSpamming("updates", 1000)) {
                UserCommunication.Notify("Now checking for updates, you will be notified when it's done", MessageImg.MsgInfo, "Update", "Update check", 5);
                GetLatestReleaseInfo(true);
            }
        }

        /// <summary>
        /// Gets an object with the latest release info
        /// </summary>
        public static void GetLatestReleaseInfo(bool alwaysGetFeedBack) {
            _alwaysGetFeedBack = alwaysGetFeedBack;

            if (_checkStarted) {
                return;
            } else if (LatestReleaseInfo != null) {
                // we already checked and there is a new version
                NotifyUpdateAvailable();
                return;
            }

            _checkStarted = true;
            try {
                using (WebClient wc = new WebClient()) {
                    wc.Proxy = Config.Instance.GetWebClientProxy();
                    wc.Headers.Add("user-agent", "Mozilla/4.0 (compatible; MSIE 6.0; Windows NT 5.2; .NET CLR 1.0.3705;)");
                    //wc.Proxy = null;

                    // Download release list from GITHUB API 
                    string json = "";
                    try {
                        json = wc.DownloadString(Config.ReleasesUrl);
                    } catch (WebException e) {
                        UserCommunication.Notify("For your information, I couldn't manage to retrieve the latest published version on github.<br><br>A request has been sent to :<br><a href='" + Config.ReleasesUrl + "'>" + Config.ReleasesUrl + "</a><br>but was unsuccessul, you might have to check for a new version manually if this happens again.", MessageImg.MsgHighImportance, "Couldn't reach github", "Connection failed");
                        ErrorHandler.Log(e.ToString());
                    }

                    // Parse the .json
                    var parser = new JsonParser(json);
                    parser.Tokenize();
                    var releasesList = parser.GetList();

                    // Releases list empty?
                    if (releasesList == null) {
                        _checkStarted = false;
                        return;
                    }

                    var localVersion = AssemblyInfo.Version;

                    var outputBody = new StringBuilder();
                    var highestVersion = localVersion;
                    var highestVersionInt = -1;
                    var iCount = 0;
                    foreach (var release in releasesList) {
                        var releaseVersionTuple = release.FirstOrDefault(tuple => tuple.Item1.Equals("tag_name"));
                        var prereleaseTuple = release.FirstOrDefault(tuple => tuple.Item1.Equals("prerelease"));
                        var releaseNameTuple = release.FirstOrDefault(tuple => tuple.Item1.Equals("name"));

                        if (releaseVersionTuple != null && prereleaseTuple != null) {

                            var releaseVersion = releaseVersionTuple.Item2;

                            // is it the highest version ? for prereleases or full releases depending on the user config
                            if (((Config.Instance.UserGetsPreReleases && prereleaseTuple.Item2.EqualsCi("true"))
                                 || (!Config.Instance.UserGetsPreReleases && prereleaseTuple.Item2.EqualsCi("false")))
                                && releaseVersion.IsHigherVersionThan(highestVersion)) {
                                highestVersion = releaseVersion;
                                highestVersionInt = iCount;
                            }

                            // For each version higher than the local one, append to the release body
                            // Will be used to display the version log to the user
                            if (releaseVersion.IsHigherVersionThan(localVersion)) {
                                outputBody.AppendLine("\n\n## " + releaseVersion + ((releaseNameTuple != null) ? " : " + releaseNameTuple.Item2 : "") + " ##\n\n");
                                var locBody = release.FirstOrDefault(tuple => tuple.Item1.Equals("body"));
                                if (locBody != null)
                                    outputBody.AppendLine(locBody.Item2);
                            }
                        }
                        iCount++;
                    }

                    // There is a distant version higher than the local one
                    if (highestVersionInt > -1) {
                        // Update dir
                        if (Directory.Exists(PathUpdateFolder))
                            Utils.DeleteDirectory(PathUpdateFolder, true);
                        Directory.CreateDirectory(PathUpdateFolder);

                        // latest release info
                        try {
                            LatestReleaseInfo = new ReleaseInfo(
                                releasesList[highestVersionInt].First(tuple => tuple.Item1.Equals("tag_name")).Item2,
                                releasesList[highestVersionInt].First(tuple => tuple.Item1.Equals("name")).Item2,
                                releasesList[highestVersionInt].First(tuple => tuple.Item1.Equals("prerelease")).Item2.EqualsCi("true"),
                                releasesList[highestVersionInt].First(tuple => tuple.Item1.Equals("html_url")).Item2,
                                outputBody.ToString().Replace("\\r\\n", "\n").Replace("\\n", "\n"),
                                releasesList[highestVersionInt].First(tuple => tuple.Item1.Equals("draft")).Item2.EqualsCi("true"),
                                releasesList[highestVersionInt].First(tuple => tuple.Item1.Equals("updated_at")).Item2.Substring(0, 10)
                                );
                        } catch (Exception x) {
                            ErrorHandler.DirtyLog(x);
                        }

                        // Hookup DownloadFileCompleted Event, download the release .zip
                        var downloadUriTuple = releasesList[highestVersionInt].FirstOrDefault(tuple => tuple.Item1.Equals("browser_download_url"));
                        if (downloadUriTuple != null) {
                            wc.DownloadFileCompleted += WcOnDownloadFileCompleted;
                            wc.DownloadFileAsync(new Uri(downloadUriTuple.Item2), PathLatestReleaseZip);
                        }

                    } else if (_alwaysGetFeedBack) {
                        UserCommunication.Notify("Congratulations! You already possess the latest version of 3P!", MessageImg.MsgOk, "Update check", "You own the version " + AssemblyInfo.Version);
                    }
                }
            } catch (Exception e) {
                ErrorHandler.ShowErrors(e, "GetLatestReleaseInfo");
            }

            _checkStarted = false;
        }

        /// <summary>
        /// Called when the latest release download is done
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="asyncCompletedEventArgs"></param>
        private static void WcOnDownloadFileCompleted(object sender, AsyncCompletedEventArgs asyncCompletedEventArgs) {
            try {
                // copy 7zip.exe
                if (!File.Exists(Path7ZipExe))
                    File.WriteAllBytes(Path7ZipExe, Resources._7z);
                if (!File.Exists(Path7ZipDll))
                    File.WriteAllBytes(Path7ZipDll, Resources._7zdll);

                // Extract the .zip file
                var process = Process.Start(new ProcessStartInfo {
                    FileName = Path7ZipExe,
                    Arguments = string.Format("x -y \"-o{0}\" \"{1}\"", Path.Combine(Npp.GetConfigDir(), "Update"), PathLatestReleaseZip),
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    WindowStyle = ProcessWindowStyle.Hidden
                });
                if (process != null)
                    process.WaitForExit();

                // check the presence of the plugin file
                if (!File.Exists(PathDownloadedPlugin)) {
                    Utils.DeleteDirectory(PathUpdateFolder, true);
                    return;
                }

                // copy the 3pUpdater.exe, one or the other version depending if we need admin rights
                if (!File.Exists(PathUpdaterExe))
                    File.WriteAllBytes(PathUpdaterExe, Utils.IsDirectoryWritable(Path.GetDirectoryName(AssemblyInfo.Location)) ? Resources._3pUpdater_user : Resources._3pUpdater);

                // configure the update
                File.WriteAllText(PathUpdaterLst, string.Join("\t", PathDownloadedPlugin, AssemblyInfo.Location), Encoding.Default);

                // write the version log
                File.WriteAllText(PathToVersionLog, LatestReleaseInfo.Body, Encoding.Default);

                NotifyUpdateAvailable();

            } catch (Exception e) {
                ErrorHandler.ShowErrors(e, "WcOnDownloadFileCompleted");
            }
        }

        private static void NotifyUpdateAvailable() {
            UserCommunication.Notify(@"Dear user, <br>
                <br>
                A new version of 3P is available on github and will be automatically installed the next time you restart Notepad++<br>
                <br>
                Your version: <b>" + AssemblyInfo.Version + @"</b><br>
                Distant version: <b>" + LatestReleaseInfo.Version + @"</b><br>
                Release name: <b>" + LatestReleaseInfo.Name + @"</b><br>
                Available since: <b>" + LatestReleaseInfo.ReleaseDate + @"</b><br>
                Release URL: <b><a href='" + LatestReleaseInfo.ReleaseUrl + "'>" + LatestReleaseInfo.ReleaseUrl + @"</a></b><br>" +
                ((Config.Instance.UserGetsPreReleases && LatestReleaseInfo.IsPreRelease) ? "This distant release is not flagged as a stable version<br>" : ""), MessageImg.MsgUpdate, "Update check", "An update is available");
        }

        /// <summary>
        /// Method to call when the user leaves notepad++,
        /// check if there is an update to make
        /// </summary>
        public static void OnNotepadExit() {
            if (File.Exists(PathUpdaterExe)) {
                var process = new Process {
                    StartInfo = {
                        FileName = PathUpdaterExe,
                        Arguments = PathDownloadedPlugin.ProgressQuoter() + " " + AssemblyInfo.Location.ProgressQuoter(),
                        UseShellExecute = true,
                        WindowStyle = ProcessWindowStyle.Hidden
                    }
                };
                process.Start();
            }
        }

        /// <summary>
        /// Method to call when the user starts notepad++,
        /// check if an update has been done since the last time notepad was closed
        /// </summary>
        public static void OnNotepadStart() {

            // if the UDL is not installed
            if (!Style.InstallUdl(true)) {
                Style.InstallUdl();
            } else {
                // first use message?
                if (Config.Instance.UserFirstUse) {
                    UserCommunication.Notify("First use of the program, display a smart message explaining where to start and why he should deactivate npp's default auto-completion", MessageImg.MsgInfo, "Information", "Welcome!");
                    Config.Instance.UserFirstUse = false;
                }
            }

            // an update has been done
            if (File.Exists(PathToVersionLog)) {
                if (File.Exists(PathDownloadedPlugin)) {
                    UserCommunication.Notify(@"<h2>I require your attention!</h2><br>
                        The update didn't go as expected, i couldn't replace the old plugin file by the new one!<br>
                        It is very likely because i didn't get the rights to write a file in your /plugins/ folder, don't panic!<br>
                        You will have to manually copy the new file and delete the old file :<br><br>
                        <b>Move</b> this file : <a href='" + Path.GetDirectoryName(PathDownloadedPlugin) + "'>" + PathDownloadedPlugin + @"</a></b><br>" + @"
                        In this folder (replacing the old file) : <a href='" + Path.GetDirectoryName(AssemblyInfo.Location) + "'>" + Path.GetDirectoryName(AssemblyInfo.Location) + @"</a><br>
                        Please do it as soon as possible, as i will stop checking for more updates until this problem is fixed.<br>
                        Thank you for your patience!<br>", MessageImg.MsgUpdate, "Update", "Problem during the update!");
                    return;
                }

                UserCommunication.Message(("# What's new in this version? #\n\n" + File.ReadAllText(PathToVersionLog, TextEncodingDetect.GetFileEncoding(PathToVersionLog))).MdToHtml(),
                    MessageImg.MsgUpdate,
                    "A new version has been installed!",
                    "Updated to version " + AssemblyInfo.Version,
                    new List<string> { "ok" },
                    false);

                try {
                    File.Delete(PathToVersionLog);
                    if (Directory.Exists(PathUpdateFolder))
                        Utils.DeleteDirectory(PathUpdateFolder, true);
                } catch (Exception e) {
                    ErrorHandler.ShowErrors(e, "Error when deleting a file/folder");
                }

                // update UDL
                if (!Config.Instance.GlobalDontUpdateUdlOnUpdate)
                    Style.InstallUdl();
            }

            // Check for new updates
            if (!Config.Instance.GlobalDontCheckUpdates)
                Task.Factory.StartNew(GetLatestReleaseInfo);
        }

    }

    /// <summary>
    /// Contains info about the latest release
    /// </summary>
    public class ReleaseInfo {
        public string Version { private set; get; }
        public string Name { private set; get; }
        public bool IsPreRelease { private set; get; }
        public bool IsDraft { private set; get; }
        public string ReleaseUrl { private set; get; }
        public string Body { private set; get; }
        public string ReleaseDate { private set; get; }

        public ReleaseInfo(string version, string name, bool isPreRelease, string releaseUrl, string body, bool isDraft, string releaseDate) {
            Version = version;
            Name = name;
            IsPreRelease = isPreRelease;
            ReleaseUrl = releaseUrl;
            Body = body;
            IsDraft = isDraft;
            ReleaseDate = releaseDate;
        }
    }

}