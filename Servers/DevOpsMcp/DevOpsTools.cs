using System.ComponentModel;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;

namespace DevOpsMcp;

[McpServerToolType]
public sealed class DevOpsTools(DevOpsWorkItemService service)
{
    [McpServerTool(Name = "tfs_fetch_work_item"), Description("Fetch a single Azure DevOps/TFS work item with formatted details.")]
    public Task<string> FetchWorkItem(
        [Description("Numeric Azure DevOps/TFS work item ID to fetch.")] int work_item_id) =>
        service.FetchWorkItemAsync(work_item_id);

    [McpServerTool(Name = "tfs_search_work_items"), Description("Search Azure DevOps/TFS work items by keyword, optionally filtered by type.")]
    public Task<string> SearchWorkItems(
        [Description("Keyword to search in title or description.")] string query,
        [Description("Optional work item type filter.")] string? work_item_type = null,
        [Description("Maximum number of results to return.")] int limit = 10) =>
        service.SearchWorkItemsAsync(query, work_item_type, limit);

    [McpServerTool(Name = "tfs_list_sprint_items"), Description("List Azure DevOps/TFS work items for a specific sprint iteration path.")]
    public Task<string> ListSprintItems(
        [Description("Azure DevOps iteration path.")] string iteration_path,
        [Description("Maximum number of results to return.")] int limit = 20) =>
        service.ListSprintItemsAsync(iteration_path, limit);
}
