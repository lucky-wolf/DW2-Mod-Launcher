using System.Runtime.CompilerServices;

// Lets DW2ModLauncher.Tests unit test the internal pieces of the Workshop-publish "wizardry"
// (ModPublishOutputParser, ModWorkshopIdWatcher, ChildConsoleCapture) without making them part of
// the public API surface - only IModPublisher/ModPublishResult are meant to be called from
// outside this assembly.
[assembly: InternalsVisibleTo("DW2ModLauncher.Tests")]
