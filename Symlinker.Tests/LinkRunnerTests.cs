namespace Symlinker.Tests
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Threading.Tasks;

    using Res = Symlinker.Properties.Resources;

    public sealed class LinkRunnerTests
    {
        private readonly FakeDialogs dialogs = new();
        private readonly List<FakeLinkHelper> started = new();
        private readonly List<LinkRequest> unelevated = new();

        private bool symlinksNeedAdmin = true;
        private int helperChanges;

        // What the next start returns: a running helper by default, null for a declined prompt
        private Func<FakeLinkHelper> nextHelper = () => new FakeLinkHelper();

        private LinkRunner CreateRunner() => new(
            dialogs,
            () => symlinksNeedAdmin,
            () =>
            {
                var helper = nextHelper();
                if (helper != null)
                    started.Add(helper);
                return Task.FromResult<ILinkHelper>(helper);
            },
            request =>
            {
                unelevated.Add(request);
                return Task.FromResult(new LinkResult { Success = true, Message = "unelevated" });
            },
            () => helperChanges++);

        private static LinkRequest Request(string type) => new() { Type = type, Link = @"C:\links\a", Target = @"D:\target", Directory = true };

        [Theory]
        [InlineData("junction", true)]
        [InlineData("hard", true)]
        [InlineData("symbolic", false)]
        public async Task Prepare_DoesNotAsk_WhenTheLinkNeedsNoAdminRights(string type, bool needAdmin)
        {
            symlinksNeedAdmin = needAdmin;
            using var runner = CreateRunner();

            Assert.True(await runner.PrepareAsync(Request(type)));
            Assert.Empty(started);
            Assert.False(runner.HelperRunning);
            Assert.Equal(0, helperChanges);
        }

        [Fact]
        public async Task Prepare_StartsTheHelperOnce_ForSymbolicLinksThatNeedAdmin()
        {
            using var runner = CreateRunner();

            Assert.True(await runner.PrepareAsync(Request("symbolic")));
            Assert.True(await runner.PrepareAsync(Request("symbolic")));

            Assert.Single(started);
            Assert.True(runner.HelperRunning);
            Assert.Equal(1, helperChanges);
        }

        [Fact]
        public async Task Prepare_ReplacesAHelperThatStopped()
        {
            using var runner = CreateRunner();
            await runner.PrepareAsync(Request("symbolic"));
            started[0].IsAlive = false;

            Assert.False(runner.HelperRunning);
            Assert.True(await runner.PrepareAsync(Request("symbolic")));

            Assert.Equal(2, started.Count);
            Assert.True(started[0].Disposed);
            Assert.True(runner.HelperRunning);
        }

        [Fact]
        public async Task Prepare_StopsQuietly_WhenThePromptIsDeclined()
        {
            nextHelper = () => null;
            using var runner = CreateRunner();

            Assert.False(await runner.PrepareAsync(Request("symbolic")));

            Assert.False(runner.HelperRunning);
            Assert.Empty(dialogs.Messages);
        }

        [Fact]
        public async Task Prepare_ShowsAnError_WhenTheHelperCantStart()
        {
            nextHelper = () => throw new InvalidOperationException("helper broke");
            using var runner = CreateRunner();

            Assert.False(await runner.PrepareAsync(Request("symbolic")));

            Assert.Equal((Res.MessageBoxErrorTitle, Res.MessageBoxExceptionOcurred + "\nhelper broke"), dialogs.Last);
            Assert.Equal(0, helperChanges);
        }

        [Fact]
        public async Task Run_SendsSymbolicLinksThatNeedAdmin_ToTheHelper()
        {
            using var runner = CreateRunner();
            await runner.PrepareAsync(Request("symbolic"));

            var symbolic = await runner.RunAsync(Request("symbolic"));
            var junction = await runner.RunAsync(Request("junction"));

            Assert.Equal("elevated", symbolic.Message);
            Assert.Equal("unelevated", junction.Message);
            Assert.Single(started[0].Calls);
            Assert.Equal("junction", Assert.Single(unelevated).Type);
        }

        [Fact]
        public async Task Run_StaysUnelevated_WithoutAHelper_OrOnceAdminIsNoLongerNeeded()
        {
            using var runner = CreateRunner();
            await runner.RunAsync(Request("symbolic"));

            await runner.PrepareAsync(Request("symbolic"));
            symlinksNeedAdmin = false;
            await runner.RunAsync(Request("symbolic"));

            Assert.Equal(2, unelevated.Count);
            Assert.Empty(started[0].Calls);
        }

        [Fact]
        public async Task Run_ReportsErrors_AsAFailedLink()
        {
            var runner = new LinkRunner(dialogs, () => false, () => Task.FromResult<ILinkHelper>(null), _ => throw new IOException("pipe broke"), () => { });

            var result = await runner.RunAsync(Request("junction"));

            Assert.Equal((false, Res.MessageBoxExceptionOcurred + "\npipe broke"), (result.Success, result.Message));
        }

        [Fact]
        public async Task Dispose_ClosesTheHelper()
        {
            var runner = CreateRunner();
            runner.Dispose();

            await runner.PrepareAsync(Request("symbolic"));
            runner.Dispose();

            Assert.True(started[0].Disposed);
            Assert.False(runner.HelperRunning);
        }
    }
}
