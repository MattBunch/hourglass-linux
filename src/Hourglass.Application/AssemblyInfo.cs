using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("Hourglass.Application.Tests")]
// Temporary migration bridge: the GUI composes sessions until the shared runtime owns registration.
[assembly: InternalsVisibleTo("hourglass-linux")]
