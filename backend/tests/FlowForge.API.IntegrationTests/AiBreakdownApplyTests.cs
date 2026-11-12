using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FlowForge.Application.Identity.DTOs;
using FlowForge.Application.Projects.DTOs;
using FlowForge.Domain.Projects.Enums;
using FluentAssertions;

namespace FlowForge.API.IntegrationTests;

/// <summary>
/// "Apply AI breakdown" turns reviewed suggestions into real subtasks. It does not call the AI provider (the
/// suggestions are sent in the request), so it can be tested end to end without any model.
/// </summary>
[Collection("Integration")]
public class AiBreakdownApplyTests
{
    private readonly CustomWebApplicationFactory _factory;
    public AiBreakdownApplyTests(CustomWebApplicationFactory factory) => _factory = factory;

    private static string Tag() => Guid.NewGuid().ToString("N")[..8];

    private async Task<(HttpClient Client, BoardDto Board, TaskCardDto Task, Guid ProjectId)> SetupAsync()
    {
        var tag = Tag();
        var client = _factory.CreateClient();
        var auth = await (await client.PostAsJsonAsync("/api/v1/auth/register", new RegisterRequest(
            FirstName: "Breakdown", LastName: "Tester", Email: $"breakdown-{tag}@integration.test",
            Password: "Passw0rd123", TenantName: $"Breakdown Co {tag}", TenantSlug: $"breakdown-{tag}")))
            .Content.ReadFromJsonAsync<AuthResponse>();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        var project = (await (await client.PostAsJsonAsync("/api/v1/projects", new
        {
            name = "Breakdown Project",
            key = "BRK" + tag[..4].ToUpperInvariant(),
            description = (string?)null,
            color = (string?)null,
            visibility = (int)ProjectVisibility.TenantWide
        })).Content.ReadFromJsonAsync<ProjectDto>())!;

        var board = (await (await client.GetAsync($"/api/v1/projects/{project.Id}/boards"))
            .Content.ReadFromJsonAsync<List<BoardDto>>())![0];

        var task = await CreateTaskAsync(client, project.Id, board, "Parent task");
        return (client, board, task, project.Id);
    }

    private static async Task<TaskCardDto> CreateTaskAsync(HttpClient client, Guid projectId, BoardDto board, string title)
    {
        var response = await client.PostAsJsonAsync("/api/v1/tasks", new
        {
            projectId,
            boardId = board.Id,
            listId = board.Lists.Single(l => l.Name == "To Do").Id,
            title,
            description = (string?)null,
            type = (int)TaskType.Task,
            priority = (int)TaskPriority.Medium
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<TaskCardDto>())!;
    }

    private static async Task<List<TaskCardDto>> AllTasksAsync(HttpClient client, Guid boardId)
    {
        var board = (await (await client.GetAsync($"/api/v1/boards/{boardId}")).Content.ReadFromJsonAsync<BoardDto>())!;
        return board.Lists.SelectMany(l => l.Tasks).ToList();
    }

    [Fact]
    public async Task Applying_several_suggestions_creates_every_subtask_with_its_own_number()
    {
        var (client, board, parent, _) = await SetupAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/tasks/{parent.Id}/ai/breakdown/apply",
            new { subtaskTitles = new[] { "Design the screen", "Build the API", "Write the tests", "Review and ship" } });

        response.StatusCode.Should().Be(HttpStatusCode.OK, "the original bug returned a 500 on the second subtask");
        (await response.Content.ReadFromJsonAsync<int>()).Should().Be(4);

        var tasks = await AllTasksAsync(client, board.Id);
        tasks.Should().HaveCount(5); // the parent plus four subtasks
        tasks.Select(t => t.TaskNumber).Should().OnlyHaveUniqueItems();
        tasks.Where(t => t.ParentTaskId == parent.Id).Select(t => t.Title).Should().BeEquivalentTo(
            "Design the screen", "Build the API", "Write the tests", "Review and ship");
    }

    [Fact]
    public async Task Numbers_keep_counting_after_a_batch_so_the_next_task_does_not_collide()
    {
        var (client, board, parent, projectId) = await SetupAsync();

        (await client.PostAsJsonAsync($"/api/v1/tasks/{parent.Id}/ai/breakdown/apply",
            new { subtaskTitles = new[] { "One", "Two", "Three" } })).EnsureSuccessStatusCode();
        var next = await CreateTaskAsync(client, projectId, board, "Created after the batch");
        (await client.PostAsJsonAsync($"/api/v1/tasks/{parent.Id}/ai/breakdown/apply",
            new { subtaskTitles = new[] { "Four", "Five" } })).EnsureSuccessStatusCode();

        var numbers = (await AllTasksAsync(client, board.Id)).Select(t => t.TaskNumber).ToList();
        numbers.Should().HaveCount(7).And.OnlyHaveUniqueItems();
        numbers.Should().Contain(next.TaskNumber);
    }

    [Fact]
    public async Task Blank_suggestions_are_rejected_without_creating_anything()
    {
        var (client, board, parent, _) = await SetupAsync();

        var response = await client.PostAsJsonAsync($"/api/v1/tasks/{parent.Id}/ai/breakdown/apply",
            new { subtaskTitles = new[] { "Valid", "" } });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await AllTasksAsync(client, board.Id)).Should().HaveCount(1);
    }
}
