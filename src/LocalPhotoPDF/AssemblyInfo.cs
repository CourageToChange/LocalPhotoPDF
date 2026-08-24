using System.Runtime.CompilerServices;
using System.Windows;

// Settings types stay `internal` - they are not a public API - but the tests need to reach them
// so the folder-fallback and upgrade behaviour can be covered without opening a window.
[assembly: InternalsVisibleTo("LocalPhotoPDF.Tests")]

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None,            //where theme specific resource dictionaries are located
                                                //(used if a resource is not found in the page,
                                                // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly   //where the generic resource dictionary is located
                                                //(used if a resource is not found in the page,
                                                // app, or any theme specific resource dictionaries)
)]
