using System.IO.MemoryMappedFiles;
using Launchly.App;

namespace Launchly.Tests;

public class SingleInstanceTests
{
    [Fact]
    public void First_WritesPid_EvenWhenPidMappingAlreadyExists()
    {
        var name = "LaunchlyTest" + Guid.NewGuid().ToString("N");
        using var stale = MemoryMappedFile.CreateNew($@"Local\{name}.Pid", sizeof(int));   // artık/önceden var olan eşleme

        using var instance = new SingleInstance(name);

        Assert.True(instance.IsFirst);
        using var file = MemoryMappedFile.OpenExisting($@"Local\{name}.Pid", MemoryMappedFileRights.Read);
        using var view = file.CreateViewAccessor(0, sizeof(int), MemoryMappedFileAccess.Read);
        Assert.Equal(Environment.ProcessId, view.ReadInt32(0));
    }
}
