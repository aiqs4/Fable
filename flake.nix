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
          url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/${sdkVersion}/dotnet-sdk-${sdkVersion}-linux-x64.tar.gz";
          hash = "sha512:b7b3d8461cf98b07d2552ce9199702fa3ad010ac2671a5f8cd5136f7534e50f6855ba7bbb02b11494563f1d04156276a31cda1103b900e5eecd0d9473dc7b9c5";
        };
        aarch64-linux = {
          url = "https://builds.dotnet.microsoft.com/dotnet/Sdk/${sdkVersion}/dotnet-sdk-${sdkVersion}-linux-arm64.tar.gz";
          hash = "sha512:0966aa2b70f4677b8319be9df447a78a0dcd6164836425b55a68fd714f4023feb87b4cac21b9a996b79472ad54487cd0adcd2a9dfd570e2b2a1d26423a2baa9b";
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
