using System.ClientModel;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.AI;
using OpenAI;
using ProductDescGen.Data;
using System.Collections.Generic;
using System;

namespace ProductDescGen;

public sealed record GenerationResult(ProductDescription Data, long InputTokenCount, long OutputTokenCount, long ReasoningTokenCount, long TotalTokenCount);

public sealed class AiClient
{
    private const string EmptyKey = "EMPTY";
    private const string DefaultSystemPrompt =
        "You are an expert industrial parts copywriter and SEO specialist. I will provide you with a Product name or part number and its condition.\n\nYou must return a valid, parseable JSON object with exactly these four keys:\n- `product_title`: A clean, descriptive title for the product page.\n- `meta_title`: An SEO-optimized title (maximum 60 characters) starting with the part number.\n- `meta_description`: A SEO-optimized meta description (maximum 160 characters) that includes the part number and condition.\n- `html_description`: A professional technical product description formatted strictly in HTML. It must include a brief introductory `<p>`, a `<ul>` list of technical specifications or inferred features based on the part number, and a `<strong>` tag highlighting the condition. Do not include `<html>` or `<body>` tags, just the raw HTML content.\n\nOutput ONLY the raw JSON object. Do not include any conversational text.";
    private static readonly Uri EmptyEndpoint = new("https://example.com");

    private string _systemMessage;
    private ReasoningEffort? _reasoningEffort;
    private OpenAIClient _client;
    private readonly ApiKeyCredential _credential = new(EmptyKey);

    public AiClient()
    {
        _client = new(_credential, new() { Endpoint = EmptyEndpoint });
        _systemMessage = DefaultSystemPrompt;
    }

    public void SetEndpoint(string endpoint)
    {
        _client = new(_credential, new() { Endpoint = new(endpoint) });
    }

    public void SetApiKey(string key)
    {
        _credential.Update(key);
    }

    public void SetReasoningLevel(ReasoningEffort? level)
    {
        _reasoningEffort = level;
    }

    public void SetSystemPrompt(string prompt)
    {
        _systemMessage = prompt;
    }

    public void ResetSystemPrompt()
    {
        _systemMessage = DefaultSystemPrompt;
    }

    public string GetSystemPrompt()
    {
        return _systemMessage;
    }

    public async Task<List<string>> GetModelsAsync()
    {
        var modelClient = _client.GetOpenAIModelClient();
        var result = await modelClient.GetModelsAsync();
        var modelNames = result.Value.Select(m => m.Id).Order().ToList();

        return modelNames;
    }

    public async Task<GenerationResult> GenerateDescription(string model, string productName, string productCondition)
    {
        ChatMessage systemPrompt = new(ChatRole.System, _systemMessage);
        ChatMessage userPrompt = new(ChatRole.User, $"Product name: {productName}\nCondition: {productCondition}");
        ChatMessage[] chatHistory = [systemPrompt, userPrompt];
        ChatOptions options = new()
        {
            Reasoning = new() { Effort = _reasoningEffort, Output = ReasoningOutput.None }
        };

        var chatClient = _client.GetChatClient(model).AsIChatClient();
        var response = await chatClient.GetResponseAsync<ProductDescription>(chatHistory, ProductDataGenerationContext.Default.Options, options);

        if (!response.TryGetResult(out var generatedProductData))
        {
            throw new Exception("Model did not return valid JSON");
        }

        return new(
            generatedProductData,
            response.Usage?.InputTokenCount ?? 0,
            response.Usage?.OutputTokenCount ?? 0,
            response.Usage?.ReasoningTokenCount ?? 0,
            response.Usage?.TotalTokenCount ?? 0);
    }
}