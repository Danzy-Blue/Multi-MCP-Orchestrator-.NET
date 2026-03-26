using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace UwMcp;

[McpServerToolType]
public sealed class UwTools(UwToolService service)
{
    [McpServerTool(Name = "add"), Description("Adds two numbers.")]
    public static string Add(
        [Description("First integer.")] int a,
        [Description("Second integer.")] int b) => (a + b).ToString();

    [McpServerTool(Name = "get_submission"), Description("Get a submission by contract reference.")]
    public Task<string> GetSubmission(
        [Description("Submission contract reference.")] string contract_refrence = "RSK0023130501Q001") =>
        service.GetSubmissionAsync(contract_refrence);

    [McpServerTool(Name = "get_quote"), Description("Get a quote by contract reference.")]
    public Task<string> GetQuote(
        [Description("Quote contract reference.")] string contract_refrence = "RSK0023130501Q001") =>
        service.GetQuoteAsync(contract_refrence);

    [McpServerTool(Name = "create_submission"), Description("Create a submission entry.")]
    public Task<string> CreateSubmission(
        [Description("Organisation.")] string organisation,
        [Description("Inception date.")] string inceptionDate,
        [Description("New or renewal.")] string newRenewal) =>
        service.CreateSubmissionAsync(organisation, inceptionDate, newRenewal);

    [McpServerTool(Name = "create_submission_from_json"), Description("Create a submission entry from a JSON DTO.")]
    public Task<string> CreateSubmissionFromJson(
        [Description("Submission DTO payload.")] JsonObject submission_json) =>
        service.CreateSubmissionFromJsonAsync(submission_json);

    [McpServerTool(Name = "edit_submission_from_json"), Description("Edit or update a submission entry from a JSON DTO.")]
    public Task<string> EditSubmissionFromJson(
        [Description("Submission reference.")] string submission_refrence,
        [Description("Submission DTO payload.")] JsonObject submission_json) =>
        service.EditSubmissionFromJsonAsync(submission_refrence, submission_json);
}
