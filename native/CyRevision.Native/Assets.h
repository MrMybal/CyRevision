#pragma once
#include <imgui.h>
#include <string>
struct ID3D11Device;
namespace cyrevision {
struct NativeTexture {ImTextureID id=0;int width=0,height=0;};
void initNativeTextures(ID3D11Device* device);
void beginNativeTexturesFrame();
NativeTexture* nativeTexture(const std::string& absolutePath);
void releaseNativeTextures();
}
