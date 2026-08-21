# Tesearis.SmolVDecoder

A standalone .NET library for decoding SMOL-V-compressed SPIR-V modules back into raw SPIR-V. 

## Build

Build the DLL with the .NET SDK:

```
dotnet build -c Release src/Tesearis.SmolVDecoder
```

or, from the repo root, build the whole solution (library + tests):

```
dotnet build -c Release Tesearis.SmolVDecoder.slnx
```

The output is `src/Tesearis.SmolVDecoder/bin/Release/netstandard2.0/Tesearis.SmolVDecoder.dll`, a plain netstandard2.0/net8.0 library you can
reference from any .NET project.

To use it inside Unity specifically, drop the netstandard2.0 DLL into a Unity project's
`Assets/Plugins/Editor` folder (or any folder named `Editor`), then in the Inspector's Plugin settings restrict it to the Editor platform — it reads
Unity's Vulkan shader cache during editor/build-pipeline tooling, not something you'd ship into players.

## Testing

```
dotnet test
```

## Usage

```csharp
if (Tesearis.SmolVDecoder.SmolV.TryDecodeStages(compiledData, out var vertexSpirv, out var fragmentSpirv, out var error))
{
    // vertexSpirv / fragmentSpirv are raw SPIR-V bytes
}
```

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).

### Third-party code

This project contains a modified, decode-only C# port of portions of
[SMOL-V](https://github.com/aras-p/smol-v) by Aras Pranckevicius.

SMOL-V is made available under the MIT License or a public-domain dedication. This project uses the MIT-licensed option. The applicable attribution
and license text are included in [ThirdPartyNotices.md](ThirdPartyNotices.md).