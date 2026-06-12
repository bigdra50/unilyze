# Release automation replaces the __...__ placeholders and attaches the rendered
# formula to each GitHub Release. Copy that rendered file to
# bigdra50/homebrew-tap/Formula/unilyze.rb after publishing a tag.
class Unilyze < Formula
  desc "Static analyzer for Unity and general C# projects"
  homepage "https://github.com/bigdra50/unilyze"
  version "__VERSION__"
  license "MIT"

  on_macos do
    if Hardware::CPU.arm?
      url "https://github.com/bigdra50/unilyze/releases/download/v__VERSION__/unilyze-__VERSION__-osx-arm64.tar.gz"
      sha256 "__OSX_ARM64_SHA256__"
    else
      url "https://github.com/bigdra50/unilyze/releases/download/v__VERSION__/unilyze-__VERSION__-osx-x64.tar.gz"
      sha256 "__OSX_X64_SHA256__"
    end
  end

  on_linux do
    url "https://github.com/bigdra50/unilyze/releases/download/v__VERSION__/unilyze-__VERSION__-linux-x64.tar.gz"
    sha256 "__LINUX_X64_SHA256__"
  end

  def install
    bin.install "unilyze"
  end

  test do
    assert_match version.to_s, shell_output("#{bin}/unilyze --version")
  end
end
