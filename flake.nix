{
  description = "Pack aiqs4/Fable (feature/wasmgc-backend) as downloadable NuGet packages";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs =
    { self, nixpkgs }:
    let
      # Match global.json and use the latest .NET 10 SDK band so the bundled
      # F# compiler includes the generic type-extension constraint fix.
      sdkVersion = "10.0.403";
      sdkBySystem = {
        x86_64-linux = {
          url = "https://download.visualstudio.microsoft.com/download/pr/2b94f7b4-9ffd-419f-b38d-2e16ebd31eee/04ea4379e2c9af0b8f2c7af062bdb555ad6ba65cfab9c7d26e7f4370b8dd3063/dotnet-sdk-${sdkVersion}-linux-x64.tar.gz";
          hash = "sha512:f8be2a3b45e53fa86500da5c40ed63d74ef7cff417f3e823b8568a87137e092de8ab02b1676f8af2cda227ecbc5deac6f3f60a95db99fcc801dbca2b0592c6ef081983f5df183179b6c1680a9412f85ebad1cc6a8c943ad2be3b1cdeff330e6b";
        };
        aarch64-linux = {
          url = "https://download.visualstudio.microsoft.com/download/pr/60e64c2b-c87d-403e-b4b7-a27aa0b1c1e6/42a25e5b4b0e54a2e8e4bcf59e0e2b0f9812cef2a8c978c4e65ca2a0a6c5936d/dotnet-sdk-${sdkVersion}-linux-arm64.tar.gz";
          hash = "sha512:ac2a6b2bde4c82718a8bcb61adb7b65b12b40ab9241fe4b2a47bcea368bae542b226314872b81332eee64e5c08e9c6279658a904df2af93c35968cebcfc92552c758e80ada5192d83323a87ba39a28bbe7725baa1eaae7d38ffeabf311b40a8b";
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
