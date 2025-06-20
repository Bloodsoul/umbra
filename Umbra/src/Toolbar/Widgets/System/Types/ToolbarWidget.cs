﻿using ImGuiNET;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Umbra.Common;
using Umbra.Widgets.System;
using Una.Drawing;

namespace Umbra.Widgets;

public abstract class ToolbarWidget(
    WidgetInfo                  info,
    string?                     guid         = null,
    Dictionary<string, object>? configValues = null
) : IDisposable
{
    [ConfigVariable(
        "Toolbar.PopupActivationMethod",
        "General",
        "Toolbar",
        options: ["ClickAndHover", "Click", "Hover"]
    )]
    private static string PopupActivationMethod { get; set; } = "ClickAndHover";

    internal event Action<IWidgetConfigVariable>?      OnConfigValueChanged;
    internal event Action<ToolbarWidget, WidgetPopup>? OpenPopup;
    internal event Action<ToolbarWidget, WidgetPopup>? OpenPopupDelayed;

    /// <summary>
    /// The unique identifier of this widget instance.
    /// </summary>
    public string Id { get; } = guid ?? Guid.NewGuid().ToString();

    /// <summary>
    /// Defines the toolbar panel where this widget is located.
    /// </summary>
    public string Location { get; set; } = "Left";

    /// <summary>
    /// Defines the sort index within the toolbar panel.
    /// </summary>
    public int SortIndex { get; set; }

    /// <summary>
    /// Returns the information object of this widget.
    /// </summary>
    public WidgetInfo Info { get; } = info;

    /// <summary>
    /// Defines the node of this widget.
    /// </summary>
    public abstract Node Node { get; }

    /// <summary>
    /// Defines the popup of this widget. Setting a value will make the widget
    /// interactive and will render the popup node when the widget is clicked.
    /// </summary>
    public abstract WidgetPopup? Popup { get; }

    /// <summary>
    /// Returns the safe height of the widget so that it always fits within the
    /// height of the toolbar.
    /// </summary>
    protected static int SafeHeight => Toolbar.Height - 4;

    protected bool IsVisible { get; set; } = true;
    
    private readonly Dictionary<string, IWidgetConfigVariable> _configVariables = new();
    private readonly Dictionary<string, object>                _configValues    = configValues ?? [];

    public bool IsDisposed { get; private set; }

    public void Setup()
    {
        foreach (var cfg in GetConfigVariablesInternal()) {
            _configVariables[cfg.Id] = cfg;

            if (cfg is IUntypedWidgetConfigVariable u) {
                if (false == _configValues.ContainsKey(cfg.Id)) {
                    _configValues[cfg.Id] = u.GetDefaultValue()!;
                }

                u.UntypedValueChanged += value => _configValues[cfg.Id] = value;
            }
        }

        if (_configValues.Count > 0) {
            List<string> keysToRemove = [];

            foreach ((string key, object value) in _configValues) {
                if (!_configVariables.TryGetValue(key, out IWidgetConfigVariable? cfg)) {
                    keysToRemove.Add(key);
                    continue;
                }

                if (cfg is IUntypedWidgetConfigVariable u) {
                    if (cfg is IEnumWidgetConfigVariable e) {
                        try {
                            Type enumType = e.GetType().GenericTypeArguments[0];
                            u.SetValue(Enum.ToObject(enumType, value));
                        } catch {
                            keysToRemove.Add(key); // Faulty config.
                            Logger.Warning($"Failed to set config value '{key}' to '{value}' in widget '{Info.Name}'.");
                        }
                    } else {
                        u.SetValue(value);
                    }
                }
            }

            foreach (string key in keysToRemove) {
                _configValues.Remove(key);
            }
        }

        Initialize();

        Node.ToggleClass("widget-instance", true);
        
        if (Popup is null) return;

        Node.OnMouseDown += _ => {
            if (Node.IsDisabled) return;

            if (WidgetManager.EnableQuickSettingAccess && ImGui.GetIO().KeyCtrl && ImGui.GetIO().KeyShift) {
                return;
            }

            OpenPopup?.Invoke(this, Popup);
        };

        Node.OnDelayedMouseEnter += _ => {
            if (Node.IsDisabled || PopupActivationMethod != "ClickAndHover") return;
            OpenPopupDelayed?.Invoke(this, Popup);
        };

        Node.OnMouseEnter += _ => {
            if (Node.IsDisabled || PopupActivationMethod != "Hover") return;
            OpenPopup?.Invoke(this, Popup);
        };
        
        OnConfigurationChanged();
    }

    /// <summary>
    /// Returns the name of this widget instance.
    /// Derived classes can override this method to provide a custom name.
    /// </summary>
    /// <returns></returns>
    public virtual string GetInstanceName()
    {
        return Info.Name;
    }

    public void Update()
    {
        Node.Style.IsVisible = IsEnabled && IsVisible;
        Node.SortIndex       = SortIndex;

        Node.Style.Anchor = Location switch {
            "Left"   => Anchor.MiddleLeft,
            "Center" => Anchor.MiddleCenter,
            "Right"  => Anchor.MiddleRight,
            _        => Anchor.MiddleCenter,
        };

        if (!IsEnabled) return;

        try {
            OnUpdate();
        } catch (Exception e) {
            Logger.Error(e.Message);
            Logger.Error(e.StackTrace);
        }
    }

    /// <summary>
    /// Returns a dictionary of configuration values for this widget instance.
    /// </summary>
    /// <returns></returns>
    public Dictionary<string, object> GetUserConfig()
    {
        return _configValues;
    }

    /// <summary>
    /// Returns a list of registered config options.
    /// </summary>
    /// <returns></returns>
    public List<IWidgetConfigVariable> GetConfigVariableList()
    {
        return _configVariables.Values.ToList();
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        foreach (var handler in OnConfigValueChanged?.GetInvocationList() ?? []) {
            OnConfigValueChanged -= (Action<IWidgetConfigVariable>)handler;
        }

        foreach (var handler in OpenPopup?.GetInvocationList() ?? []) {
            OpenPopup -= (Action<ToolbarWidget, WidgetPopup>)handler;
        }

        foreach (var handler in OpenPopupDelayed?.GetInvocationList() ?? []) {
            OpenPopupDelayed -= (Action<ToolbarWidget, WidgetPopup>)handler;
        }

        OnConfigValueChanged = null;
        OpenPopup            = null;
        OpenPopupDelayed     = null;

        foreach (var cfg in _configVariables.Values) {
            if (cfg is IUntypedWidgetConfigVariable u) {
                u.Dispose();
            }
        }

        OnDisposed();

        Popup?.Dispose();
        Node.Dispose();

        _configValues.Clear();
        _configVariables.Clear();
    }

    /// <summary>
    /// Invoked when the widget is being disposed.
    /// </summary>
    protected virtual void OnDisposed() { }

    /// <summary>
    /// Invoked when the configuration of this widget has changed.
    /// </summary>
    protected virtual void OnConfigurationChanged() { }

    /// <summary>
    /// Initialization method that is called when the widget is created and
    /// initial configuration data has been set.
    /// </summary>
    protected abstract void Initialize();

    /// <summary>
    /// Invoked on every frame, just before the widget is rendered.
    /// </summary>
    protected abstract void OnUpdate();
    
    /// <summary>
    /// Returns a list of configuration variables for this widget that the user
    /// can modify.
    /// </summary>
    protected abstract IEnumerable<IWidgetConfigVariable> GetConfigVariables();

    public T GetConfigValue<T>(string name)
    {
        if (!_configVariables.TryGetValue(name, out var cfg)) {
            throw new InvalidOperationException($"No config variable with the name '{name}' exists.");
        }

        if (cfg is not WidgetConfigVariable<T> c) {
            throw new InvalidOperationException($"Config variable '{name}' is not of type '{typeof(T).Name}'.");
        }

        if (typeof(T) == typeof(string) && cfg is StringWidgetConfigVariable { SupportsScripting: true } str) {
            return (T)(object)str.EvaluatedValue;
        }

        return c.Value;
    }

    /// <summary>
    /// Returns true of a config variable with the given name exists.
    /// </summary>
    public bool HasConfigVariable(string name)
    {
        return _configVariables.ContainsKey(name);
    }

    /// <summary>
    /// Returns true if this widget is currently enabled.
    /// </summary>
    /// <returns></returns>
    public bool IsEnabled => !HasConfigVariable("_Enabled") || GetConfigValue<bool>("_Enabled");

    public void SetConfigValue<T>(string name, T value)
    {
        if (!_configVariables.TryGetValue(name, out var cfg)) {
            throw new InvalidOperationException($"No config variable with the name '{name}' exists.");
        }

        if (cfg is not WidgetConfigVariable<T> c) {
            throw new InvalidOperationException($"Config variable '{name}' is not of type '{typeof(T).Name}'.");
        }

        c.Value = value;

        Framework.Service<WidgetManager>().SaveWidgetState(Id);
        Framework.Service<WidgetManager>().SaveState();

        OnConfigValueChanged?.Invoke(c);
        OnConfigurationChanged();
    }

    public void CopyInstanceSettingsToClipboard()
    {
        var config = Convert.ToBase64String(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(_configValues)));

        ImGui.SetClipboardText($"WI|{Info.Id}|{config}");
    }

    public void PasteInstanceSettingsFromClipboard()
    {
        string? clipboard = ImGui.GetClipboardText();

        if (string.IsNullOrEmpty(clipboard)) return;
        if (!clipboard.StartsWith("WI|")) return;

        string[] parts = clipboard.Split('|');

        if (parts.Length < 3) return;
        if (parts[1] != Info.Id) return;

        string config = Encoding.UTF8.GetString(Convert.FromBase64String(parts[2]));

        Dictionary<string, object>? values = JsonConvert.DeserializeObject<Dictionary<string, object>>(config);
        if (values == null) return;

        foreach ((string key, object value) in values) {
            if (_configVariables.TryGetValue(key, out IWidgetConfigVariable? cfg)) {
                if (cfg is IUntypedWidgetConfigVariable u) {
                    u.SetValue(value);
                    OnConfigValueChanged?.Invoke(cfg);
                }
            }
        }

        Framework.Service<WidgetManager>().SaveWidgetState(Id);
        Framework.Service<WidgetManager>().SaveState();
    }

    /// <summary>
    /// Returns a dictionary of color options for the user to select from in a
    /// configuration variable.
    /// </summary>
    protected static Dictionary<string, string> GetColorSelectOptions()
    {
        Dictionary<string, string> result = [];

        foreach (string id in Color.GetAssignedNames()) {
            result[id] = I18N.Translate($"Color.{id}.Name");
        }

        return result;
    }
    
    protected bool IsMemberOfVerticalBar => Node.ParentNode?.ParentNode?.ClassList.Contains("vertical") ?? false;

    /// <summary>
    /// Opens the settings window for this widget instance.
    /// </summary>
    internal void OpenSettingsWindow()
    {
        Framework.Service<WidgetInstanceEditor>().OpenEditor(this);
    }

    private IEnumerable<IWidgetConfigVariable> GetConfigVariablesInternal()
    {
        return [
            new BooleanWidgetConfigVariable(
                "_Enabled",
                I18N.Translate("Widget.Defaults.Enabled.Name"),
                I18N.Translate("Widget.Defaults.Enabled.Description"),
                true
            ),
            ..GetConfigVariables(),
            ..Popup?.GetConfigVariables() ?? [],
        ];
    }
}
