{
  description = "Pack aiqs4/Fable (feature/wasmgc-backend) as downloadable NuGet packages";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      # global.json on this branch pins SDK 10.0.300 with rollForward=latestPatch,
      # which stays inside the 10.0.3xx band. nixpkgs' sdk_10_0 is 10.0.1xx and
      # cannot satisfy that pin, so the SDK tarball is fetched directly.
      sdkVersion = "10.0.303";
      sdkBySystem = {
        x86_64-linux = {
          url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/${sdkVersion}/dotnet-sdk-${sdkVersion}-linux-x64.tar.gz";
          hash = "sha512-U43xHOudhr3aBhurbt49H21Gu5ugh66QVkPz1Fav6YKUnRuVK86zBaXSLS1uacwOUQAhb/zHCaqoboZ/LZzfmg==";
        };
        aarch64-linux = {
          url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/${sdkVersion}/dotnet-sdk-${sdkVersion}-linux-arm64.tar.gz";
          hash = "sha512-Yi4FkZgFjuX5LXgXvdg4hUgKl5YoNI+HggHLjbpS0WjBoyT5lDoOegOtVH2FMyqoLxas7SnaWZ7wJCK1N4RS3g==";
        };
      };
      systems = builtins.attrNames sdkBySystem;
      each = f: nixpkgs.lib.genAttrs systems (system: f (import nixpkgs { inherit system; }));
    in
    {
      packages = each (
        pkgs:
        let
          system = pkgs.stdenv.hostPlatform.system;
          dotnetSdk = pkgs.stdenvNoCC.mkDerivation {
            pname = "dotnet-sdk";
            version = sdkVersion;
            src = pkgs.fetchurl sdkBySystem.${system};
            dontConfigure = true;
            dontBuild = true;
            dontFixup = true;
            sourceRoot = ".";
            installPhase = ''
              runHook preInstall
              mkdir -p "$out"
              shopt -s dotglob
              mv ./* "$out"/
              runHook postInstall
            '';
          };
          pack = pkgs.writeShellApplication {
            name = "pack";
            runtimeInputs = [
              pkgs.git
              pkgs.zip
              pkgs.cacert
              pkgs.python3
            ];
            text = ''
              export DOTNET_ROOT=${dotnetSdk}
              export PATH="$DOTNET_ROOT:$PATH"
              export SSL_CERT_FILE=${pkgs.cacert}/etc/ssl/certs/ca-bundle.crt
              export DOTNET_CLI_TELEMETRY_OPTOUT=1
              export DOTNET_NOLOGO=1
              export DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1
            ''
            + builtins.readFile ./nix/pack-nugets.sh;
          };
        in
        {
          inherit dotnetSdk pack;
          default = pack;
        }
      );
    };
}
