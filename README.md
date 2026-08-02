# unity-smolv-decoder
Unity Editor utility for decoding SMOL-V-compressed SPIR-V modules from Vulkan shader compilation output.

## Build

Build the DLL with the .NET SDK:

```
dotnet build -c Release src/SmolVDecoder
```

or, from the repo root, build the whole solution (library + tests):

```
dotnet build -c Release SmolVDecoder.slnx
```

The output is `src/SmolVDecoder/bin/Release/netstandard2.0/SmolVDecoder.dll`. Drop it into a Unity
project's `Assets/Plugins/Editor` folder (or any folder named `Editor`), then in the Inspector's
Plugin settings restrict it to the Editor platform, since this is meant to run inside the Unity
Editor / build pipeline only, not in players.

## Testing

```
dotnet test
```

## Usage

```csharp
if (SmolVDecoder.SmolV.TryDecodeStages(compiledData, out var vertexSpirv, out var fragmentSpirv, out var error))
{
    // vertexSpirv / fragmentSpirv are raw SPIR-V bytes
}
```

## License

This project is licensed under the MIT License. See [LICENSE](LICENSE).

### Third-party code

This project contains a modified, decode-only C# port of portions of
[SMOL-V](https://github.com/aras-p/smol-v) by Aras Pranckevicius.

SMOL-V is made available under the MIT License or a public-domain dedication.
This project uses the MIT-licensed option. The applicable attribution and
license text are included in [ThirdPartyNotices.md](ThirdPartyNotices.md).