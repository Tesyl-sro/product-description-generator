using System;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media;
using ProductDescGen.Data;
using Microsoft.Extensions.AI;
using ProductDescGen.Utils;

namespace ProductDescGen.Windows;

public partial class MainWindow : Window
{
    private readonly AiClient _client;
    private readonly UpdateChecker _updateChecker;
    private readonly AppSettings _savedSettings = SettingsStore.Load();

    public MainWindow()
    {
        InitializeComponent();

        // Create a new client
        _client = new();
        
        // Create update checker
        _updateChecker = new(GetAppVersion());

        // Set the system prompt to the default one
        SystemPromptTextBox.Text = _client.GetSystemPrompt();

        // Restore settings
        RestoreSettings();
        
        // Set up model picker
        SetUpModelPicker();

        // Display app version
        VersionTextBlock.Text = $"v{GetAppVersion()}";

        // Check for updates once the window is shown
        Opened += CheckForUpdates;

        // Unchecking "Allow editing" discards any edits and restores the default prompt
        AllowEditSystemPromptCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (AllowEditSystemPromptCheckBox.IsChecked != true)
                SystemPromptTextBox.Text = _client.GetSystemPrompt();
        };

        // Update client's system prompt when it's changed
        SystemPromptTextBox.TextChanged += SystemPromptChanged;

        // Hook up the `GenerateDescription()` method to run on button click
        GenerateButton.Click += GenerateDescription;

        // Hook up the `ClearUiState()` method to run on button click
        ClearButton.Click += ClearUiState;

        // Delete the stored settings and put the settings panel back to its defaults
        ResetSettingsButton.Click += ResetSettings;

        // Copy to clipboard handle
        CopyButton.Click += CopyHtmlToClipboard;

        // Update client when the provider is changed
        ProviderComboBox.SelectionChanged += ProviderChanged;

        // Update API key in client when it's changed
        ApiKeyTextBox.TextChanged += ApiKeyChanged;

        // Update reasoning in client when it's changed
        ReasoningLevelComboBox.SelectionChanged += ReasoningLevelChanged;

        // Toggle between the raw HTML and a rendered preview
        HtmlPreviewButton.Click += (_, _) => ShowHtmlPreview(!HtmlPreviewBorder.IsVisible);

        // Re-create the client and fetch the model list
        FetchModelsButton.Click += TryUpdateModels;
    }

    private void ProviderChanged(object? obj, SelectionChangedEventArgs args)
    {
        var endpoint = ProviderComboBox.SelectedIndex switch
        {
            0 => "https://generativelanguage.googleapis.com/v1beta/openai/",
            1 => "https://openrouter.ai/api/v1",
            2 => "https://api.venice.ai/api/v1",
            3 => "https://api.openai.com/v1",
            _ => throw new ArgumentOutOfRangeException(nameof(ProviderComboBox.SelectedIndex))
        };

        _client.SetEndpoint(endpoint);
    }

    private void ApiKeyChanged(object? obj, TextChangedEventArgs args)
    {
        _client.SetApiKey(ApiKeyTextBox.Text ?? string.Empty);
    }

    private void SystemPromptChanged(object? obj, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(SystemPromptTextBox.Text))
        {
            _client.ResetSystemPrompt();
            return;
        }

        _client.SetSystemPrompt(SystemPromptTextBox.Text);
    }

    private void ReasoningLevelChanged(object? obj, SelectionChangedEventArgs args)
    {
        ReasoningEffort? level = ReasoningLevelComboBox.SelectedIndex switch
        {
            0 => null,
            1 => ReasoningEffort.None,
            2 => ReasoningEffort.Low,
            3 => ReasoningEffort.Medium,
            4 => ReasoningEffort.High,
            5 => ReasoningEffort.ExtraHigh,
            _ => throw new ArgumentOutOfRangeException(nameof(ReasoningLevelComboBox.SelectedIndex))
        };

        _client.SetReasoningLevel(level);
    }

    private async void TryUpdateModels(object? obj, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(ApiKeyTextBox.Text))
        {
            SetErrorStatus("Please enter your API key.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ModelAutoCompleteBox.Text))
        {
            SetErrorStatus("Please select a model.");
            return;
        }
        
        SetProviderConfigUiEnabled(false);
        SetStatus("Updating models...");

        try
        {
            var models = await _client.GetModelsAsync();
            ModelAutoCompleteBox.ItemsSource = models;
            SetStatus("Ready.");
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to fetch models: {CleanUpException(ex.Message)}");
        }
        finally
        {
            SetProviderConfigUiEnabled(true);
        }
    }

    private void SetUpModelPicker()
    {
        // Filter by substring, but when the text is just the already-picked model, show the
        // whole list so the user can switch models without clearing the box first
        ModelAutoCompleteBox.ItemFilter = (search, item) =>
            item is string id &&
            (string.IsNullOrEmpty(search)
             // compare against the picked item, not Text: Text always equals the search string
             || search == ModelAutoCompleteBox.SelectedItem as string
             || id.Contains(search, StringComparison.OrdinalIgnoreCase));

        // Open the list when the text part is clicked. Clicks inside the dropdown itself also
        // bubble up to the AutoCompleteBox, so ignore those: reopening the list right after an
        // item was picked is what made a different item end up selected.
        ModelAutoCompleteBox.AddHandler(PointerReleasedEvent, (_, e) =>
        {
            if (e.Source is Visual v
                && ModelAutoCompleteBox.FindDescendantOfType<TextBox>() is { } textBox
                && (v == textBox || textBox.IsVisualAncestorOf(v)))
            {
                OpenModelDropDown();
            }
        }, RoutingStrategies.Bubble, handledEventsToo: true);

        // Open on Tab-in too, but not when focus returns to the box after picking an item
        ModelAutoCompleteBox.GotFocus += (_, e) =>
        {
            if (e.NavigationMethod == NavigationMethod.Tab)
                OpenModelDropDown();
        };
    }

    private void OpenModelDropDown()
    {
        if (ModelAutoCompleteBox.IsDropDownOpen || !ModelAutoCompleteBox.IsEnabled)
            return;

        // Select the text so typing replaces the current model instead of appending to it
        if (ModelAutoCompleteBox.FindDescendantOfType<TextBox>() is { } textBox)
            textBox.SelectAll();

        ModelAutoCompleteBox.IsDropDownOpen = true;
    }

    private async void CopyHtmlToClipboard(object? obj, RoutedEventArgs args)
    {
        if (string.IsNullOrEmpty(HtmlTextBox.Text))
        {
            SetErrorStatus("Nothing to copy. Generate a description first.");
            return;
        }

        if (Clipboard is not { } clipboard)
        {
            SetErrorStatus("System clipboard is not available.");
            return;
        }

        try
        {
            await clipboard.SetTextAsync(HtmlTextBox.Text);
            SetStatus("HTML copied to clipboard.");
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to copy: {CleanUpException(ex.Message)}");
        }
    }

    private void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.ClearValue(TextBlock.ForegroundProperty);
        StatusTextBlock.ClearValue(TextBlock.FontWeightProperty);
    }

    private void SetErrorStatus(string text)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.Foreground = Brushes.Red;
        StatusTextBlock.FontWeight = FontWeight.Bold;
    }

    private async void GenerateDescription(object? obj, RoutedEventArgs args)
    {
        if (string.IsNullOrWhiteSpace(ApiKeyTextBox.Text))
        {
            SetErrorStatus("Please enter your API key.");
            return;
        }

        if (string.IsNullOrWhiteSpace(ModelAutoCompleteBox.Text))
        {
            SetErrorStatus("Please select a model.");
            return;
        }

        var name = ProductNameTextBox.Text?.Trim();
        var condition = ConditionTextBox.Text?.Trim();

        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(condition))
        {
            SetErrorStatus("Enter a valid product name or part number, and condition");
            return;
        }

        SetStatus($"Generating with {ModelAutoCompleteBox.Text}...");
        var stopwatch = Stopwatch.StartNew();
        SetUiEnabled(false);

        try
        {
            var result = await _client.GenerateDescription(ModelAutoCompleteBox.Text, name, condition);

            ProductTitleTextBox.Text = result.Data.ProductTitle;
            MetaTitleTextBox.Text = result.Data.MetaTitle;
            MetaDescriptionTextBox.Text = result.Data.MetaDescription;
            HtmlTextBox.Text = result.Data.HtmlDescription;
            ShowHtmlPreview(false);

            var timeElapsed = stopwatch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture);
            SetStatus($"Done. Took {timeElapsed}s. Token usage: {result.InputTokenCount} in / {result.OutputTokenCount} out / {result.ReasoningTokenCount} reasoning / {result.TotalTokenCount} total");
            SaveSettings();
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to generate: {CleanUpException(ex.Message)}");
        }
        finally
        {
            SetUiEnabled(true);
        }
    }

    private void RestoreSettings()
    {
        ProviderComboBox.SelectedIndex = _savedSettings.ProviderIndex;
        ApiKeyTextBox.Text = _savedSettings.ApiKey;
        ModelAutoCompleteBox.Text = _savedSettings.PreferredModel;
        ReasoningLevelComboBox.SelectedIndex = _savedSettings.ReasoningLevelIndex;

        ProviderChanged(null, null!);
        ReasoningLevelChanged(null, null!);

        if (_savedSettings.CustomSystemPrompt is not null)
        {
            SystemPromptTextBox.Text = _savedSettings.CustomSystemPrompt;
            AllowEditSystemPromptCheckBox.IsChecked = true;
        }
    }

    private void SaveSettings()
    {
        try
        {
            SettingsStore.Save(new(
                ProviderComboBox.SelectedIndex,
                ApiKeyTextBox.Text,
                ModelAutoCompleteBox.Text,
                AllowEditSystemPromptCheckBox.IsChecked == true ? SystemPromptTextBox.Text : null,
                ReasoningLevelComboBox.SelectedIndex));
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to save settings: {CleanUpException(ex.Message)}");
        }
    }

    private void ResetSettings(object? obj, RoutedEventArgs args)
    {
        try
        {
            SettingsStore.Delete();
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to reset settings: {CleanUpException(ex.Message)}");
            return;
        }

        // Put the settings panel back to its defaults; unchecking "Allow editing" restores the default prompt
        ApiKeyTextBox.Text = string.Empty;
        ProviderComboBox.SelectedIndex = 0;
        ReasoningLevelComboBox.SelectedIndex = 0;
        AllowEditSystemPromptCheckBox.IsChecked = false;

        SetStatus("Settings reset.");
    }

    private void SetProviderConfigUiEnabled(bool enabled)
    {
        ProviderComboBox.IsEnabled = enabled;
        ModelAutoCompleteBox.IsEnabled = enabled;
        ApiKeyTextBox.IsEnabled = enabled;
        AllowEditSystemPromptCheckBox.IsEnabled = enabled;
        SystemPromptTextBox.IsEnabled = enabled && (AllowEditSystemPromptCheckBox.IsChecked ?? false);
        FetchModelsButton.IsEnabled = enabled;
    }

    private void SetUiEnabled(bool enabled)
    {
        GenerateButton.IsEnabled = enabled;
        ClearButton.IsEnabled = enabled;
        CopyButton.IsEnabled = enabled;
        HtmlTextBox.IsEnabled = enabled;
        HtmlPreviewButton.IsEnabled = enabled;
        MetaTitleTextBox.IsEnabled = enabled;
        MetaDescriptionTextBox.IsEnabled = enabled;
        ProductTitleTextBox.IsEnabled = enabled;
        ProductNameTextBox.IsEnabled = enabled;
        ConditionTextBox.IsEnabled = enabled;
        ResetSettingsButton.IsEnabled = enabled;
    }

    private void ClearUiState(object? obj, RoutedEventArgs args)
    {
        HtmlTextBox.Text = string.Empty;
        MetaDescriptionTextBox.Text = string.Empty;
        MetaTitleTextBox.Text = string.Empty;
        ProductTitleTextBox.Text = string.Empty;
        ShowHtmlPreview(false);
    }

    private void ShowHtmlPreview(bool show)
    {
        if (show)
            HtmlPreviewBlock.Inlines = HtmlToInlineConverter.Build(HtmlTextBox.Text ?? string.Empty);
        else
            HtmlPreviewBlock.Inlines?.Clear();

        HtmlPreviewBorder.IsVisible = show;
        HtmlTextBox.IsVisible = !show;
        HtmlPreviewButton.Content = show ? "Show HTML" : "HTML Preview";
    }

    private async void CheckForUpdates(object? sender, EventArgs e)
    {
        var update = await _updateChecker.CheckForUpdateAsync();
        if (update is null)
        {
            return;
        }

        VersionTextBlock.Text = $"v{GetAppVersion()} → v{update.Latest} available";
        VersionTextBlock.Opacity = 1;
        VersionTextBlock.Foreground = Brushes.DodgerBlue;
        VersionTextBlock.Cursor = new Cursor(StandardCursorType.Hand);
        ToolTip.SetTip(VersionTextBlock, "Update available! Open the release page");
        VersionTextBlock.PointerPressed += (_, _) =>
            Process.Start(new ProcessStartInfo(update.Url) { UseShellExecute = true });
    }

    private static string GetAppVersion()
    {
        var version = typeof(MainWindow).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        return string.IsNullOrEmpty(version) ? "?" : version.Split('+')[0];
    }

    private static string CleanUpException(string raw) => raw.Trim().ReplaceLineEndings(" ");
}