#region License Information (GPL v3)

/*
    XerahS - The Avalonia UI implementation of ShareX
    Copyright (c) 2007-2026 ShareX Team

    This program is free software; you can redistribute it and/or
    modify it under the terms of the GNU General Public License
    as published by the Free Software Foundation; either version 2
    of the License, or (at your option) any later version.

    This program is distributed in the hope that it will be useful,
    but WITHOUT ANY WARRANTY; without even the implied warranty of
    MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
    GNU General Public License for more details.

    You should have received a copy of the GNU General Public License
    along with this program; if not, write to the Free Software
    Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301, USA.

    Optionally you can also view the license at <http://www.gnu.org/licenses/>.
*/

#endregion License Information (GPL v3)

using NUnit.Framework;
using XerahS.Assistant.Models;
using XerahS.Assistant.Services;
using XerahS.UI.ViewModels;

namespace XerahS.Tests.UI;

[TestFixture]
public class AssistantViewModelTests
{
    private static readonly AssistantAction Action = new(AssistantActionKind.CopyText, "Copy");

    [Test]
    public async Task OlderResponse_DoesNotReplaceANewerRequest()
    {
        var service = new PendingAssistantService();
        var viewModel = new AssistantViewModel(service) { Prompt = "first" };

        Task first = viewModel.SubmitCommand.ExecuteAsync(null);
        Task second = viewModel.ExecuteActionCommand.ExecuteAsync(Action);

        service.Requests[0].SetResult(AssistantResponse.Info("first response"));
        await first;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsBusy, Is.True);
            Assert.That(viewModel.StatusText, Is.Not.EqualTo("first response"));
        });

        service.Requests[1].SetResult(AssistantResponse.Info("second response"));
        await second;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsBusy, Is.False);
            Assert.That(viewModel.StatusText, Is.EqualTo("second response"));
        });
    }

    [Test]
    public async Task CancelledOlderRequest_DoesNotEndANewerRequest()
    {
        var service = new PendingAssistantService();
        var viewModel = new AssistantViewModel(service) { Prompt = "first" };

        Task first = viewModel.SubmitCommand.ExecuteAsync(null);
        Task second = viewModel.ExecuteActionCommand.ExecuteAsync(Action);
        string statusOfNewerRequest = viewModel.StatusText;

        service.Requests[0].SetCanceled();
        await first;

        Assert.Multiple(() =>
        {
            Assert.That(viewModel.IsBusy, Is.True);
            Assert.That(viewModel.StatusText, Is.EqualTo(statusOfNewerRequest));
        });

        service.Requests[1].SetResult(AssistantResponse.Info("second response"));
        await second;

        Assert.That(viewModel.IsBusy, Is.False);
    }

    [Test]
    public async Task CancellingTheCurrentRequest_EndsIt()
    {
        var service = new PendingAssistantService();
        var viewModel = new AssistantViewModel(service) { Prompt = "first" };

        Task request = viewModel.SubmitCommand.ExecuteAsync(null);
        viewModel.HandleEscape();
        service.Requests[0].SetCanceled();
        await request;

        Assert.That(viewModel.IsBusy, Is.False);
    }

    private sealed class PendingAssistantService : IAssistantService
    {
        public List<TaskCompletionSource<AssistantResponse>> Requests { get; } = new();

        public Task<AssistantResponse> ProcessPromptAsync(string prompt, CancellationToken cancellationToken) => AddRequest();

        public Task<AssistantResponse> ExecuteActionAsync(AssistantAction action, bool confirmed, CancellationToken cancellationToken) => AddRequest();

        private Task<AssistantResponse> AddRequest()
        {
            var request = new TaskCompletionSource<AssistantResponse>();
            Requests.Add(request);
            return request.Task;
        }
    }
}
