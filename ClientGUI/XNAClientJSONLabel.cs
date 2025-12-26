using Rampastring.XNAUI.XNAControls;
using Rampastring.XNAUI;
using Rampastring.Tools;
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ClientCore;
using ClientCore.Extensions;

namespace ClientGUI;

public class XNAClientJSONLabel : XNALabel
{
    public string DataSourceID { get; set; }
    public string Template { get; set; }
    public string LoadingText { get; set; }
    public int MaxResults { get; set; } = 0;
    public string FallbackText { get; set; } = "N/A";

    private Action<string, bool> _dataSourceCallback;
    private bool _isSubscribed = false;

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

        TrySubscribeToDataSource();
    }

    protected override void ParseControlINIAttribute(IniFile iniFile, string key, string value)
    {
        switch (key)
        {
            case "DataSourceID":
                DataSourceID = value;
                TrySubscribeToDataSource();
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
            case "FallbackText":
                FallbackText = value.FromIniString();
                return;
        }

        base.ParseControlINIAttribute(iniFile, key, value);
    }

    private void TrySubscribeToDataSource()
    {
        if (_isSubscribed || string.IsNullOrEmpty(DataSourceID))
            return;

        _dataSourceCallback = OnDataReceived;
        bool success = JSONDataSourceManager.Instance.Subscribe(DataSourceID, _dataSourceCallback);
        if (success)
            _isSubscribed = true;
        else
            Text = FallbackText;
    }

    private void OnDataReceived(string json, bool isError)
    {
        if (isError || json == null)
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
            Logger.Log($"XNAClientJSONLabel [{Name}] processing error: {ex.Message}");
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

                string value = values.Count == 1 ? values[0] : string.Join(", ", values);

                if (!string.IsNullOrEmpty(format))
                {
                    var formatted = new List<string>();

                    foreach (var v in values)
                    {
                        if (long.TryParse(v, out long num))
                            formatted.Add(num.ToString(format));
                        else if (double.TryParse(v, out double dbl))
                            formatted.Add(dbl.ToString(format));
                        else if (DateTime.TryParse(v, out DateTime dt))
                            formatted.Add(dt.ToString(format));
                        else
                            formatted.Add(v);
                    }

                    return string.Join(", ", formatted);
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
        if (_isSubscribed && !string.IsNullOrEmpty(DataSourceID) && _dataSourceCallback != null)
        {
            JSONDataSourceManager.Instance.Unsubscribe(DataSourceID, _dataSourceCallback);
            _isSubscribed = false;
        }

        base.Kill();
    }
}