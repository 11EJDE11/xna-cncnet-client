using Rampastring.XNAUI.XNAControls;
using Rampastring.XNAUI;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClientCore.Extensions;

namespace ClientGUI;

public class XNAClientJSONLabel : XNALabel
{
    public string URL { get; set; }
    public string Template { get; set; }
    public string LoadingText { get; set; }
    public int MaxResults { get; set; } = 0;
    public int RefreshIntervalSeconds { get; set; } = 300;
    public int TimeoutSeconds { get; set; } = 10;
    public string FallbackText { get; set; } = "N/A";

    private CancellationTokenSource _loopCTS;
    private bool _fetchTaskStarted = false;

    public XNAClientJSONLabel(WindowManager windowManager) : base(windowManager)
    {
        FontIndex = 1;
    }

    public override void Initialize()
    {
        base.Initialize();

        if (!string.IsNullOrEmpty(LoadingText))
            Text = LoadingText;
        else if (!string.IsNullOrEmpty(Template))
            Text = Template;

        Logger.Log($"XNAClientJSONLabel [{Name}]: Template='{Template}', LoadingText='{LoadingText}'");
    }

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        switch (key)
        {
            case "URL":
                URL = value;
                TryStartFetchLoop();
                return;
            case "Template":
                Template = value.FromIniString();
                return;
            case "LoadingText":
                LoadingText = value.FromIniString();
                return;
            case "MaxResults":
                MaxResults = Conversions.IntFromString(value, 0);
                return;
            case "RefreshIntervalSeconds":
                RefreshIntervalSeconds = Conversions.IntFromString(value, 300);
                return;
            case "TimeoutSeconds":
                TimeoutSeconds = Conversions.IntFromString(value, 10);
                return;
            case "FallbackText":
                FallbackText = value.FromIniString();
                return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }

    private void TryStartFetchLoop()
    {
        if (!_fetchTaskStarted && !string.IsNullOrEmpty(URL))
        {
            _fetchTaskStarted = true;
            _loopCTS = new CancellationTokenSource();
            Task.Run(() => FetchLoopAsync(_loopCTS.Token));
        }
    }

    private async Task FetchLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await FetchAndUpdateAsync(token);
            }
            catch (Exception ex)
            {
                Logger.Log($"XNAClientJSONLabel error: {ex.Message}");
                WindowManager.AddCallback(() => Text = FallbackText, null);
            }

            if (RefreshIntervalSeconds <= 0)
                break;

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(RefreshIntervalSeconds), token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task FetchAndUpdateAsync(CancellationToken token)
    {
        if (token.IsCancellationRequested)
            return;

        string json = await DownloadWithTimeout(URL, TimeoutSeconds, token);
        if (json == null)
        {
            WindowManager.AddCallback(() => Text = FallbackText, null);
            return;
        }

        try
        {
            string displayText = ProcessTemplate(Template, json);
            WindowManager.AddCallback(() => Text = displayText, null);
        }
        catch (Exception ex)
        {
            Logger.Log($"XNAClientJSONLabel processing error: {ex.Message}");
            WindowManager.AddCallback(() => Text = FallbackText, null);
        }
    }

    private string ProcessTemplate(string template, string json)
    {
        if (string.IsNullOrEmpty(template))
        {
            Logger.Log($"XNAClientJSONLabel [{Name}]: Template is null or empty");
            return FallbackText;
        }

        // Regex to match {JSONPath:Format} or {JSONPath}
        // group 1 = JSONPath, group 2 = format(optional)
        var regex = new Regex(@"\{([^\}:]+)(?::([^\}]+))?\}");

        var result = regex.Replace(template, match =>
        {
            string jsonPath = match.Groups[1].Value.Trim();
            string format = match.Groups[2].Success ? match.Groups[2].Value.Trim() : null;

            try
            {
                var values = ParseJSONPath(json, jsonPath);

                if (values == null || values.Count == 0)
                    return FallbackText;

                if (MaxResults > 0 && values.Count > MaxResults)
                    values = values.GetRange(0, MaxResults);

                string value = values.Count == 1 ? values[0] : string.Join(", ", values); //fix

                if (!string.IsNullOrEmpty(format))
                {
                    if (long.TryParse(value, out long numValue))
                        return numValue.ToString(format);
                    else if (double.TryParse(value, out double doubleValue))
                        return doubleValue.ToString(format);
                    else if (DateTime.TryParse(value, out DateTime dateValue))
                        return dateValue.ToString(format);
                }

                return value;
            }
            catch (Exception ex)
            {
                Logger.Log($"XNAClientJSONLabel JSONPath '{jsonPath}' error: {ex.Message}");
                return FallbackText;
            }
        });

        return result;
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
                Logger.Log($"XNAClientJSONLabel timeout for {url}");
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
                Logger.Log($"XNAClientJSONLabel download error: {ex.Message}");
                return null;
            }
        }
    }

    private List<string> ParseJSONPath(string json, string path)
    {
        try
        {
            var root = Newtonsoft.Json.Linq.JToken.Parse(json);
            var results = new List<string>();

            var tokens = root.SelectTokens(path);

            if (tokens != null)
                foreach (var token in tokens)
                    if (token != null)
                        results.Add(token.ToString());

            return results.Count > 0 ? results : null;
        }
        catch (Exception ex)
        {
            Logger.Log($"JSONPath error: {ex.Message}");
            return null;
        }
    }

    public override void Kill()
    {
        _loopCTS?.Cancel();
        base.Kill();
    }
}