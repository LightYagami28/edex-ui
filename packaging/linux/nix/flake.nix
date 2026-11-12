{
  description = "eDEX-UI: terminale a schermo intero in stile fantascienza (binario .NET)";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs ={ self, nixpkgs }:
    let
      version = "@VERSION@";
      sources = {
        x86_64-linux = {
          url = "@URL_BASE@/edex-ui-${version}-linux-x64.tar.gz";
          hash = "@SRI_LINUX_X64@";
        };
        aarch64-linux = {
          url = "@URL_BASE@/edex-ui-${version}-linux-arm64.tar.gz";
          hash = "@SRI_LINUX_ARM64@";
        };
      };
      forAll = f: nixpkgs.lib.genAttrs (builtins.attrNames sources) (system: f nixpkgs.legacyPackages.${system} system);
    in
    {
      packages = forAll (pkgs: system: {
        default = pkgs.stdenv.mkDerivation {
          pname = "edex-ui";
          inherit version;
          src = pkgs.fetchurl sources.${system};
          nativeBuildInputs = [ pkgs.autoPatchelfHook ];
          buildInputs = [ pkgs.stdenv.cc.cc.lib pkgs.zlib pkgs.openssl ];
          # Single-file .NET: strip lo corromperebbe.
          dontStrip = true;
          installPhase = ''
            runHook preInstall
            install -Dm755 edex-ui $out/bin/edex-ui
            install -Dm644 share/applications/edex-ui.desktop $out/share/applications/edex-ui.desktop
            install -Dm644 share/icons/hicolor/256x256/apps/edex-ui.png $out/share/icons/hicolor/256x256/apps/edex-ui.png
            runHook postInstall
          '';
          meta = {
            description = "Terminale a schermo intero in stile fantascienza";
            homepage = "https://github.com/LightYagami28/edex-ui";
            license = nixpkgs.lib.licenses.gpl3Only;
            mainProgram = "edex-ui";
            platforms = builtins.attrNames sources;
          };
        };
      });
    };
}
