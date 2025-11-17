using Rampastring.Tools;

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ClientCore;

/// <summary>
/// Manages shared JSON data sources for XNAClientJSONLabel.
/// Data sources are defined in INI files under [DataSources] section.
/// </summary>
public class JSONDataSourceManager
{
    private static JSONDataSourceManager _instance;
    public static JSONDataSourceManager Instance => _instance ??= new JSONDataSourceManager();

    private readonly Dictionary<string, DataSource> _dataSources = new Dictionary<string, DataSource>();

    private JSONDataSourceManager() { }

    /// <summary>
    /// Loads data sources from an INI file's [DataSources] section.
    /// </summary>
    /// <param name="iniFile">The INI file to read from</param>
    public void LoadFromINI(IniFile iniFile)
    {
        const string sectionName = "DataSources";
        var section = iniFile.GetSection(sectionName);

        foreach (var key in section.Keys)
        {
            string value = section.GetStringValue(key.Key, string.Empty);
            if (string.IsNullOrEmpty(value))
                continue;

            // format: URL,RefreshIntervalSeconds,TimeoutSeconds,Format
            string[] parts = value.Split(',');
            if (parts.Length < 1)
                continue;

            string url = parts[0].Trim();
            int refreshInterval = parts.Length > 1 ? Conversions.IntFromString(parts[1].Trim(), 300) : 300;
            int timeout = parts.Length > 2 ? Conversions.IntFromString(parts[2].Trim(), 10) : 10;
            string format = parts.Length > 3 ? parts[3].Trim().ToLowerInvariant() : "json";

            if (string.IsNullOrEmpty(url) || _dataSources.ContainsKey(key.Key))
                continue;

            var dataSource = new DataSource(key.Key, url, refreshInterval, timeout, format);
            _dataSources.Add(key.Key, dataSource);
        }
    }

    /// <summary>
    /// Subscribes to a data source and receives JSON updates.
    /// </summary>
    /// <param name="dataSourceId">The ID of the data source</param>
    /// <param name="callback">Callback invoked when new data is available (json, isError)</param>
    /// <returns>True if subscription was successful, false if data source not found</returns>
    public bool Subscribe(string dataSourceId, Action<string, bool> callback)
    {
        if (string.IsNullOrEmpty(dataSourceId))
            return false;

        if (!_dataSources.TryGetValue(dataSourceId, out var dataSource))
            return false;

        dataSource.Subscribe(callback);
        return true;
    }

    /// <summary>
    /// Unsubscribes from a data source.
    /// </summary>
    public void Unsubscribe(string dataSourceId, Action<string, bool> callback)
    {
        if (string.IsNullOrEmpty(dataSourceId))
            return;

        if (_dataSources.TryGetValue(dataSourceId, out var dataSource))
            dataSource.Unsubscribe(callback);
    }

    private class DataSource
    {
        private readonly string _id;
        private readonly string _url;
        private readonly int _refreshIntervalSeconds;
        private readonly int _timeoutSeconds;
        private readonly string _format;
        private readonly List<Action<string, bool>> _subscribers = new List<Action<string, bool>>();
        private CancellationTokenSource _cts;
        private Task _fetchTask;
        private string _lastJson;

        public DataSource(string id, string url, int refreshIntervalSeconds, int timeoutSeconds, string format)
        {
            _id = id;
            _url = url;
            _refreshIntervalSeconds = refreshIntervalSeconds;
            _timeoutSeconds = timeoutSeconds;
            _format = format;
        }

        public void Subscribe(Action<string, bool> callback)
        {
            lock (_subscribers)
            {
                if (!_subscribers.Contains(callback))
                {
                    _subscribers.Add(callback);

                    if (_lastJson != null) // cached
                        callback(_lastJson, false);

                    if (_subscribers.Count == 1 && _fetchTask == null)
                        StartFetching();
                }
            }
        }

        public void Unsubscribe(Action<string, bool> callback)
        {
            lock (_subscribers)
            {
                _subscribers.Remove(callback);

                if (_subscribers.Count == 0)
                    StopFetching();
            }
        }

        private void StartFetching()
        {
            _cts = new CancellationTokenSource();
            _fetchTask = Task.Run(() => FetchLoopAsync(_cts.Token));
        }

        private void StopFetching()
        {
            _cts?.Cancel();
            _fetchTask = null;
        }

        private async Task FetchLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await FetchAndNotifyAsync(token);
                }
                catch (Exception ex)
                {
                    Logger.Log($"JSONDataSourceManager: Error fetching '{_id}': {ex.Message}");
                    NotifySubscribers(null, true);
                }

                if (_refreshIntervalSeconds <= 0)
                    break;

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(_refreshIntervalSeconds), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private async Task FetchAndNotifyAsync(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;

            string data = await DownloadWithTimeout(_url, _timeoutSeconds, token);

            if (data == null)
            {
                NotifySubscribers(null, true);
                return;
            }

            if (_format == "ini")
            {
                try
                {
                    data = ConvertIniToJson(data);
                }
                catch (Exception ex)
                {
                    Logger.Log($"JSONDataSourceManager: Error converting INI to JSON for '{_id}': {ex.Message}");
                    NotifySubscribers(null, true);
                    return;
                }
            }

            _lastJson = data;
            NotifySubscribers(data, false);
        }

        private string ConvertIniToJson(string iniContent)
        {
            var iniFile = new IniFile();
            using (var reader = new StringReader(iniContent))
            {
                string line;
                string currentSection = null;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (string.IsNullOrEmpty(line) || line.StartsWith(";"))
                        continue;

                    if (line.StartsWith("[") && line.EndsWith("]"))
                    {
                        currentSection = line.Substring(1, line.Length - 2);
                        continue;
                    }

                    if (currentSection != null && line.Contains("="))
                    {
                        int equalIndex = line.IndexOf('=');
                        string key = line.Substring(0, equalIndex).Trim();
                        string value = line.Substring(equalIndex + 1).Trim();
                        iniFile.SetStringValue(currentSection, key, value);
                    }
                }
            }

            var sb = new StringBuilder();
            sb.Append("{");
            bool firstSection = true;

            foreach (var sectionName in iniFile.GetSections())
            {
                var section = iniFile.GetSection(sectionName);

                if (!firstSection)
                    sb.Append(",");
                firstSection = false;

                sb.Append($"\"{EscapeJsonString(section.SectionName)}\":{{");

                bool firstKey = true;
                foreach (var key in section.Keys)
                {
                    if (!firstKey)
                        sb.Append(",");
                    firstKey = false;

                    string value = section.GetStringValue(key.Key, string.Empty);

                    if (int.TryParse(value, out int intValue))
                        sb.Append($"\"{EscapeJsonString(key.Key)}\":{intValue}");
                    else if (double.TryParse(value, out double doubleValue))
                        sb.Append($"\"{EscapeJsonString(key.Key)}\":{doubleValue}");
                    else
                        sb.Append($"\"{EscapeJsonString(key.Key)}\":\"{EscapeJsonString(value)}\"");
                }

                sb.Append("}");
            }

            sb.Append("}");
            return sb.ToString();
        }

        private string EscapeJsonString(string str)
        {
            if (string.IsNullOrEmpty(str))
                return str;

            return str.Replace("\\", "\\\\")
                      .Replace("\"", "\\\"")
                      .Replace("\n", "\\n")
                      .Replace("\r", "\\r")
                      .Replace("\t", "\\t");
        }

        private void NotifySubscribers(string json, bool isError)
        {
            lock (_subscribers)
            {
                foreach (var subscriber in _subscribers)
                {
                    try
                    {
                        subscriber(json, isError);
                    }
                    catch (Exception ex)
                    {
                        Logger.Log($"JSONDataSourceManager: Error notifying subscriber for '{_id}': {ex.Message}");
                    }
                }
            }
        }

        private async Task<string> DownloadWithTimeout(string url, int timeoutSeconds, CancellationToken token)
        {
            using (var cts = new CancellationTokenSource())
            using (var webClient = new WebClient())
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), cts.Token);
                var downloadTask = webClient.DownloadStringTaskAsync(url);

                var completed = await Task.WhenAny(downloadTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    Logger.Log($"JSONDataSourceManager: Timeout for '{_id}' ({url})");
                    return null;
                }

                cts.Cancel();

                if (token.IsCancellationRequested)
                    return null;

                try
                {
                    return await downloadTask;
                }
                catch (Exception ex)
                {
                    Logger.Log($"JSONDataSourceManager: Download error for '{_id}': {ex.Message}");
                    return null;
                }
            }
        }
    }
}