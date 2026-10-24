#include "Assets.h"
#include "Client.h"
#include <d3d11.h>
#include <wincodec.h>
#include <wrl/client.h>
#include <filesystem>
#include <unordered_map>
#include <vector>
namespace cyrevision {
namespace {
using Microsoft::WRL::ComPtr;
struct Entry {NativeTexture texture;ComPtr<ID3D11ShaderResourceView> view;std::filesystem::file_time_type modified;};
ID3D11Device* textureDevice=nullptr;
std::unordered_map<std::string,Entry> textures;
std::vector<Entry> retired;
}
void initNativeTextures(ID3D11Device* device){textureDevice=device;}
void beginNativeTexturesFrame(){retired.clear();}
void releaseNativeTextures(){textures.clear();retired.clear();textureDevice=nullptr;}
NativeTexture* nativeTexture(const std::string& absolutePath){
    if(!textureDevice || absolutePath.empty())return nullptr;
    std::error_code error;auto path=std::filesystem::path(wide(absolutePath));auto modified=std::filesystem::last_write_time(path,error);if(error)return nullptr;
    auto found=textures.find(absolutePath);if(found!=textures.end() && found->second.modified==modified)return &found->second.texture;
    ComPtr<IWICImagingFactory> factory;ComPtr<IWICBitmapDecoder> decoder;ComPtr<IWICBitmapFrameDecode> frame;ComPtr<IWICFormatConverter> converter;
    if(FAILED(CoCreateInstance(CLSID_WICImagingFactory,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&factory))) || FAILED(factory->CreateDecoderFromFilename(path.c_str(),nullptr,GENERIC_READ,WICDecodeMetadataCacheOnDemand,&decoder)) || FAILED(decoder->GetFrame(0,&frame)) || FAILED(factory->CreateFormatConverter(&converter)) || FAILED(converter->Initialize(frame.Get(),GUID_WICPixelFormat32bppRGBA,WICBitmapDitherTypeNone,nullptr,0,WICBitmapPaletteTypeCustom)))return nullptr;
    UINT width=0,height=0;if(FAILED(converter->GetSize(&width,&height)) || width==0 || height==0 || width>16384 || height>16384 || static_cast<uint64_t>(width)*height>67108864)return nullptr;
    std::vector<unsigned char> pixels(static_cast<size_t>(width)*height*4);if(FAILED(converter->CopyPixels(nullptr,width*4,static_cast<UINT>(pixels.size()),pixels.data())))return nullptr;
    D3D11_TEXTURE2D_DESC desc{};desc.Width=width;desc.Height=height;desc.MipLevels=desc.ArraySize=1;desc.Format=DXGI_FORMAT_R8G8B8A8_UNORM;desc.SampleDesc.Count=1;desc.Usage=D3D11_USAGE_IMMUTABLE;desc.BindFlags=D3D11_BIND_SHADER_RESOURCE;
    D3D11_SUBRESOURCE_DATA data{};data.pSysMem=pixels.data();data.SysMemPitch=width*4;ComPtr<ID3D11Texture2D> texture;Entry entry;
    if(FAILED(textureDevice->CreateTexture2D(&desc,&data,&texture)) || FAILED(textureDevice->CreateShaderResourceView(texture.Get(),nullptr,&entry.view)))return nullptr;
    entry.texture={static_cast<ImTextureID>(reinterpret_cast<uintptr_t>(entry.view.Get())),static_cast<int>(width),static_cast<int>(height)};entry.modified=modified;
    // Bound GPU memory across long browsing sessions. Replaced entries release COM resources.
    if(found!=textures.end()){retired.push_back(std::move(found->second));textures.erase(found);}
    if(textures.size()>=12){for(auto& old:textures)retired.push_back(std::move(old.second));textures.clear();}
    auto inserted=textures.insert_or_assign(absolutePath,std::move(entry));return &inserted.first->second.texture;
}
}
