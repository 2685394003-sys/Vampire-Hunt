using System.Runtime.CompilerServices;

// Keep mutable enemy implementation details available only to the module's
// focused EditMode tests. Production consumers use Enemies.Contracts ports.
[assembly: InternalsVisibleTo("VampireHunt.Modules.Tests")]
[assembly: InternalsVisibleTo("VampireHunt.Bootstrap")]
