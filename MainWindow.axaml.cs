using System;
using System.ClientModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Avalonia.Media;
using Microsoft.Extensions.AI;
using OpenAI;
using ProductDescGen.Data;

namespace ProductDescGen;

public partial class MainWindow : Window
{
    private const string DefaultSystemPrompt =
        "You are an expert industrial parts copywriter and SEO specialist. I will provide you with a Product name or part number and its condition.\n\nYou must return a valid, parseable JSON object with exactly these four keys:\n- `product_title`: A clean, descriptive title for the product page.\n- `meta_title`: An SEO-optimized title (maximum 60 characters) starting with the part number.\n- `meta_description`: A SEO-optimized meta description (maximum 160 characters) that includes the part number and condition.\n- `html_description`: A professional technical product description formatted strictly in HTML. It must include a brief introductory `<p>`, a `<ul>` list of technical specifications or inferred features based on the part number, and a `<strong>` tag highlighting the condition. Do not include `<html>` or `<body>` tags, just the raw HTML content.\n\nOutput ONLY the raw JSON object. Do not include any conversational text.";
    
    private OpenAIClient? _llmClient;
    private CancellationTokenSource? _updateCts;
    
    public MainWindow()
    {
        InitializeComponent();
        
        // Set the system prompt to the default one
        SystemPromptTextBox.Text = DefaultSystemPrompt;
        
        ProviderComboBox.SelectionChanged += (_, _) => OnProviderSettingsChanged();
        ApiKeyTextBox.TextChanged += (_, _) => OnProviderSettingsChanged(debounce: true);

        SetUpModelPicker();

        // Unchecking "Allow editing" discards any edits and restores the default prompt
        AllowEditSystemPromptCheckBox.IsCheckedChanged += (_, _) =>
        {
            if (AllowEditSystemPromptCheckBox.IsChecked != true)
                SystemPromptTextBox.Text = DefaultSystemPrompt;
        };
        
        // Hook up the `GenerateDescription()` method to run on button click
        GenerateButton.Click += async (_, _) => await GenerateDescription();

        // Hook up the `ClearUiState()` method to run on button click
        ClearButton.Click += (_, _) => ClearUiState();

        // Copy to clipboard handle
        CopyButton.Click += async (_, _) => await CopyHtmlToClipboard();
    }

    private void SetUpModelPicker()
    {
        // Filter by substring, but when the text is just the already-picked model, show the
        // whole list so the user can switch models without clearing the box first
        ModelAutoCompleteBox.ItemFilter = (search, item) =>
            item is string id &&
            (string.IsNullOrEmpty(search)
             || search == SelectedModel
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

    /// <summary>
    /// The model the user picked from the list, or null if the text doesn't match a known model.
    /// </summary>
    private string? SelectedModel => ModelAutoCompleteBox.SelectedItem as string;

    private async void OnProviderSettingsChanged(bool debounce = false)
    {
        _updateCts?.Cancel();
        var cts = _updateCts = new CancellationTokenSource();

        try
        {
            if (debounce)
                await Task.Delay(500, cts.Token);

            SetProviderConfigUiEnabled(false);
            await UpdateProvider();
            SetStatus("Ready.");
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            // superseded by a newer change, ignore
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to update client/models: {CleanUpException(ex.Message)}");
        }
        finally
        {
            SetProviderConfigUiEnabled(true);
        }
    }

    private async Task CopyHtmlToClipboard()
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
    
    /// <summary>
    /// Shows a normal status message in the footer, resetting any error styling.
    /// </summary>
    private void SetStatus(string text)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.ClearValue(TextBlock.ForegroundProperty);
        StatusTextBlock.ClearValue(TextBlock.FontWeightProperty);
    }

    /// <summary>
    /// Shows an error message in the footer in bold red.
    /// </summary>
    private void SetErrorStatus(string text)
    {
        StatusTextBlock.Text = text;
        StatusTextBlock.Foreground = Brushes.Red;
        StatusTextBlock.FontWeight = FontWeight.Bold;
    }
    
    private async Task UpdateProvider()
    {
        SetStatus("Updating provider...");

        var endpoint = ProviderComboBox.SelectedIndex switch
        {
            0 => new Uri("https://generativelanguage.googleapis.com/v1beta/openai/"),
            1 => new Uri("https://openrouter.ai/api/v1"),
            2 => new Uri("https://api.venice.ai/api/v1"),
            3 => new Uri("https://api.openai.com/v1"),
            _ => throw new ArgumentOutOfRangeException(nameof(ProviderComboBox.SelectedIndex))
        };

        _llmClient = new(
            new ApiKeyCredential(ApiKeyTextBox.Text ?? string.Empty),
            new() { Endpoint = endpoint });

        await UpdateModels();
    }

    private async Task UpdateModels()
    {
        SetStatus("Updating models...");
        
        var modelClient = _llmClient!.GetOpenAIModelClient();
        var result = await modelClient.GetModelsAsync();
        var modelNames = result.Value.Select(m => m.Id).Order().ToList();

        ModelAutoCompleteBox.ItemsSource = modelNames;
        ModelAutoCompleteBox.SelectedItem = null;
    }

    private async Task GenerateDescription()
    {
        if (_llmClient is null)
        {
            SetErrorStatus("No provider configured.");
            return;
        }
        if (SelectedModel is not { } model)
        {
            SetErrorStatus("Select a model from the list first.");
            return;
        }
        
        var name = ProductNameTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            SetErrorStatus("Enter a product name or part number.");
            return;
        }
        var condition = ConditionTextBox.Text?.Trim();
        
        SetStatus($"Generating with {model}...");
        var stopwatch = Stopwatch.StartNew();
        SetUiEnabled(false);

        try
        {
            var chat = _llmClient.GetChatClient(model).AsIChatClient();

            ChatResponse<ProductDescription> response = await chat.GetResponseAsync<ProductDescription>(
            [
                new ChatMessage(ChatRole.System, SystemPromptTextBox.Text ?? DefaultSystemPrompt),
                new ChatMessage(ChatRole.User,
                    $"Product name: {name}\nCondition: {(string.IsNullOrEmpty(condition) ? "not specified" : condition)}")
            ], ProductDataGenerationContext.Default.Options);

            if (!response.TryGetResult(out var result))
            {
                SetErrorStatus("The model didn't return valid JSON. Try again or pick another model.");
                return;
            }

            ProductTitleTextBox.Text = result.ProductTitle;
            MetaTitleTextBox.Text = result.MetaTitle;
            MetaDescriptionTextBox.Text = result.MetaDescription;
            HtmlTextBox.Text = result.HtmlDescription;

            var status_string =
                $"Done. Took {stopwatch.Elapsed.TotalSeconds.ToString("0.00", CultureInfo.InvariantCulture)}s.";

            if (response.Usage is { } usage)
            {
                status_string += ($" ({usage.InputTokenCount} in / {usage.OutputTokenCount} out / {usage.TotalTokenCount} total)");
            }

            SetStatus(status_string);
        }
        catch (Exception ex)
        {
            SetErrorStatus($"Failed to generate: {CleanUpException(ex.Message)}");
        }
        finally
        {
            // Re-enable the UI however generation ended (success, bad JSON or exception)
            SetUiEnabled(true);
        }
    }

    private void SetProviderConfigUiEnabled(bool enabled)
    {
        ProviderComboBox.IsEnabled = enabled;
        ModelAutoCompleteBox.IsEnabled = enabled;
        ApiKeyTextBox.IsEnabled = enabled;
        AllowEditSystemPromptCheckBox.IsEnabled = enabled;
        SystemPromptTextBox.IsEnabled = enabled && (AllowEditSystemPromptCheckBox.IsChecked ?? false);
    }

    private void SetUiEnabled(bool enabled)
    {
        GenerateButton.IsEnabled = enabled;
        ClearButton.IsEnabled = enabled;
        CopyButton.IsEnabled = enabled;
        HtmlTextBox.IsEnabled = enabled;
        MetaTitleTextBox.IsEnabled = enabled;
        MetaDescriptionTextBox.IsEnabled = enabled;
        ProductTitleTextBox.IsEnabled = enabled;
        ProductNameTextBox.IsEnabled = enabled;
        ConditionTextBox.IsEnabled = enabled;
    }

    private void ClearUiState()
    {
        HtmlTextBox.Text = string.Empty;
        MetaDescriptionTextBox.Text = string.Empty;
        MetaTitleTextBox.Text = string.Empty;
        ProductTitleTextBox.Text = string.Empty;
    }

    private static string CleanUpException(string raw) => raw.Trim().ReplaceLineEndings(" ");
}