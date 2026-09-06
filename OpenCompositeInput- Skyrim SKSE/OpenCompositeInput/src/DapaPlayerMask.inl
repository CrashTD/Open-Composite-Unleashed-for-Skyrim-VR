// Included inside the plugin namespace after the bridge definitions.
// Bridge v6 reserved bytes negotiate this new mask without accepting old R8 masks.
namespace PlayerMask {
    using GeometryFn=void(__fastcall*)(void*,RE::BSRenderPass*,uint32_t);
    using InstancedFn=void(STDMETHODCALLTYPE*)(ID3D11DeviceContext*,UINT,UINT,UINT,INT,UINT);
    GeometryFn originalSetup[2]{},originalRestore[2]{};
    DapaPlayerMaskGpu gpu;
    DapaVtableSlot hooks[4];
    std::array<DapaEngineDraw::Hook,DapaEngineDraw::sites.size()> engineHooks{};
    DapaCsxDraw::Adapter csxAdapter;
    bool csxPrepared=false;
    uint64_t csxAccepted=0,csxPlayerAccepted=0;
    std::atomic<bool> ready=false;
    bool attempted=false;
    ID3D11DeviceContext* immediate=nullptr;
    // Classification is scoped to the render pass, never cached by mesh/buffer size.
    thread_local bool owned=false;
    bool needsClear=true;
    uint64_t draws=0,classified=0;
    uint64_t higgsClassified=0;
    uint64_t spellWheelClassified=0;
    uint64_t setups=0,callbacks=0,ownedCallbacks=0;
    // Only counters on draw rejection; no per-draw logging/timing/readback.
    std::array<uint64_t,8> rejected{};
    std::array<uint64_t,DapaEngineDraw::sites.size()> siteDraws{};
    std::chrono::steady_clock::time_point lastLog{};

    bool IsPlayer(RE::BSRenderPass* pass) {
        auto* player=RE::PlayerCharacter::GetSingleton();
        if(!player || !pass || !pass->geometry)return false;
        auto* first=player->Get3D(true);auto* third=player->Get3D(false);
        const auto held=DapaHiggs::roots.Read();
        // Live ancestry handles armor equips, sex/race/model changes and VRIK's
        // third-person body without retaining freed geometry pointers.
        RE::NiAVObject* node=pass->geometry;
        const RE::TESObjectREFR* reference=nullptr;
        for(unsigned depth=0;node && depth<64;++depth,node=node->parent) {
            if(node==first || node==third)return true;
            if(DapaHiggs::roots.Matches(node,held)) {++higgsClassified;return true;}
            // Same nearest-owner semantics as CommonLib NiAVObject::GetUserData,
            // folded into our existing bounded walk (VR's declared member).
            if(!reference)reference=node->userData;
        }
        if(DapaSpellWheel::Owns(reference)) {++spellWheelClassified;return true;}
        return false;
    }
    template<int Type> void __fastcall Setup(void* shader,RE::BSRenderPass* pass,uint32_t flags) {
        owned=false;
        originalSetup[Type](shader,pass,flags);
        owned=IsPlayer(pass);
        if(owned && ++classified==1)
            SKSE::log::info("DAPA BODY MASK v4: first player-owned geometry detected ({} shader)",Type==0?"lighting":"effect");
        if((++setups & 0x1ffff)==0 && g_diagnosticLogging.load(std::memory_order_relaxed)) {
            SKSE::log::debug("DAPA BODY MASK v4: setup={} owned={} callbacks={} ownedCallbacks={} maskDraws={} CSX(accepted={},player={}) rejects(context={},notReady={},resources={},noDSV={},wrongDepth={},format={},allocation={},ownerNotAccepted={})",
                setups,classified,callbacks,ownedCallbacks,draws,csxAccepted,csxPlayerAccepted,rejected[0],rejected[1],rejected[2],rejected[3],rejected[4],rejected[5],rejected[6],rejected[7]);
        }
    }
    template<int Type> void __fastcall Restore(void* shader,RE::BSRenderPass* pass,uint32_t flags) {
        owned=false;originalRestore[Type](shader,pass,flags);
    }
    bool Prepare(ID3D11DeviceContext* ctx,bool playerDraw) {
        if(!playerDraw)return false;
        ++ownedCallbacks;
        if(ctx!=immediate) {++rejected[0];return false;}
        if(!ready.load(std::memory_order_acquire) || !g_pBridge) {++rejected[1];return false;}
        auto resources=AcquirePublishedBridgeResources();
        if(!resources.depthTexture || !resources.d3dDevice) {++rejected[2];return false;}
        Microsoft::WRL::ComPtr<ID3D11DepthStencilView> dsv;
        ctx->OMGetRenderTargets(0,nullptr,&dsv);
        if(!dsv) {++rejected[3];return false;}
        Microsoft::WRL::ComPtr<ID3D11Resource> actual;
        dsv->GetResource(&actual);
        if(actual.Get()!=resources.depthTexture) {++rejected[4];return false;} // no shadow/reflection passes
        D3D11_TEXTURE2D_DESC desc{};resources.depthTexture->GetDesc(&desc);
        if(desc.SampleDesc.Count!=1 || desc.ArraySize!=1) {++rejected[5];return false;}
        const auto* previous=gpu.Texture();
        // Invalidate publication before resource replacement. Producer and consumer
        // both run on the same immediate-context render thread.
        g_pBridge->_padPreFP[0]=0;
        if(!gpu.Size(resources.d3dDevice,desc.Width,desc.Height)) {++rejected[6];return false;}
        if(previous!=gpu.Texture() || needsClear || !g_pBridge->preFPDepthCaptured) {
            gpu.Clear(ctx);needsClear=false;
        }
        g_pBridge->preFPDepthTexture=reinterpret_cast<uint64_t>(gpu.Texture());
        return true;
    }
    void Publish() {
        ++draws;
        g_pBridge->preFPDepthCaptured=1;
        g_pBridge->_padPreFP[0]=1; // R32 raw device-depth mask v1; clear sentinel -1
        if(!g_diagnosticLogging.load(std::memory_order_relaxed))return;
        const auto now=std::chrono::steady_clock::now();
        if(now-lastLog>std::chrono::seconds(5)) {
            SKSE::log::debug("DAPA BODY MASK v4: ownedPasses={} maskDraws={} HIGGS-ownedPasses={} SpellWheel-ownedPasses={} (live ownership, no IB guessing)",classified,draws,higgsClassified,spellWheelClassified);
            lastLog=now;
        }
    }
    template<size_t Site> void STDMETHODCALLTYPE Draw(ID3D11DeviceContext* ctx,UINT n,UINT start,INT base) {
        ++callbacks;const bool playerDraw=owned;
        // Fetch CURRENT dispatch, never retain/overwrite D3D11's mutable slots.
        ctx->DrawIndexed(n,start,base);
        if(Prepare(ctx,playerDraw)) {
            if(!gpu.Replay(ctx,[&]{ctx->DrawIndexed(n,start,base);})) {++rejected[6];return;}Publish();
            if(++siteDraws[Site]==1)SKSE::log::info("DAPA BODY MASK v4: first mask at mesh draw RVA 0x{:X}",DapaEngineDraw::sites[Site].rva);
        }
    }
    template<size_t Site> void STDMETHODCALLTYPE Instanced(ID3D11DeviceContext* ctx,UINT n,UINT instances,UINT start,INT base,UINT first) {
        ++callbacks;const bool playerDraw=owned;
        DapaAcceptedDraw::Scope scope({ctx,n,instances,start,base,first},playerDraw);
        auto* previous=reinterpret_cast<InstancedFn>(engineHooks[Site].chained);
        if(previous)previous(ctx,n,instances,start,base,first); // CSX draw/suppression chain, exactly once
        else ctx->DrawIndexedInstanced(n,instances,start,base,first);
        // The CSX adapter observes accepted draws while their geometry state is
        // still bound. No acceptance means suppression/redirection: no mask.
        if(previous && playerDraw && !scope.consumed)++rejected[7];
        if(!previous && Prepare(ctx,playerDraw)) {
            if(!gpu.Replay(ctx,[&]{ctx->DrawIndexedInstanced(n,instances,start,base,first);})) {++rejected[6];return;}Publish();
            if(++siteDraws[Site]==1)SKSE::log::info("DAPA BODY MASK v4: first mask at mesh draw RVA 0x{:X}",DapaEngineDraw::sites[Site].rva);
        }
    }
    void STDMETHODCALLTYPE CsxAccepted(ID3D11DeviceContext* ctx,UINT n,UINT instances,UINT start,INT base,UINT first) {
        // Claim before dispatch to prevent nested draws/replays inheriting it.
        ++csxAccepted;
        DapaAcceptedDraw::Dispatch({ctx,n,instances,start,base,first},
            [&]{ctx->DrawIndexedInstanced(n,instances,start,base,first);},
            [&]{
                ++csxPlayerAccepted;
                if(Prepare(ctx,true)) {
                    if(!gpu.Replay(ctx,[&]{ctx->DrawIndexedInstanced(n,instances,start,base,first);})) {++rejected[6];return;}Publish();
                    if(++siteDraws[0]==1)
                        SKSE::log::info("DAPA BODY MASK v4: first CSX-accepted player draw MASKED (primary mesh site, live geometry state)");
                }
            });
    }
    template<size_t Site> uintptr_t Callback() {
        if constexpr(DapaEngineDraw::sites[Site].instanced)return reinterpret_cast<uintptr_t>(&Instanced<Site>);
        else return reinterpret_cast<uintptr_t>(&Draw<Site>);
    }
    template<size_t... Site> auto Callbacks(std::index_sequence<Site...>) {
        return std::array<uintptr_t,sizeof...(Site)>{Callback<Site>()...};
    }
    bool InstallSlot(unsigned index,void** slot,void* target,void** original,const char* name) {
        const bool ok=hooks[index].Install(slot,target,original);
        if(ok)SKSE::log::info("DAPA BODY MASK v4: {} chained; slot={} previous={}",name,static_cast<void*>(slot),*original);
        else SKSE::log::error("DAPA BODY MASK v4: {} install failed; Win32={}",name,hooks[index].error);
        if(hooks[index].protectionError)
            SKSE::log::error("DAPA BODY MASK v4: {} page protection restore failed; Win32={}",name,hooks[index].protectionError);
        return ok && !hooks[index].protectionError;
    }
    template<int Type> bool HookGeometry(uintptr_t address) {
        auto** table=reinterpret_cast<void**>(address);
        return InstallSlot(Type*2,table+6,reinterpret_cast<void*>(&Setup<Type>),
                   reinterpret_cast<void**>(&originalSetup[Type]),Type==0?"lighting setup":"effect setup") &&
            InstallSlot(1+Type*2,table+7,reinterpret_cast<void*>(&Restore<Type>),
                   reinterpret_cast<void**>(&originalRestore[Type]),Type==0?"lighting restore":"effect restore");
    }
    void RollBack() {
        ready.store(false,std::memory_order_release);
        if(!csxAdapter.Remove())
            SKSE::log::error("DAPA BODY MASK v4: CSX observer rollback failed; Win32={}",csxAdapter.error);
        for(auto& hook:engineHooks)if(!hook.Remove())
            SKSE::log::error("DAPA BODY MASK v4: engine hook rollback failed at 0x{:X}, Win32={}",hook.spec->rva,hook.error);
        for(int i=3;i>=0;--i) {
            const bool removed=hooks[i].Remove();
            if(!removed || hooks[i].protectionError)
                SKSE::log::error("DAPA BODY MASK v4: rollback slot {} failed; Win32={} protection={}",i,hooks[i].error,hooks[i].protectionError);
        }
        // Keep callback originals/context alive even if another mod already
        // chained through us. With ready=false the callbacks only pass through.
        SKSE::log::error("DAPA BODY MASK v4: startup incomplete; body correction inactive, world correction unchanged");
    }
}

void InstallSetupGeometryHook() {
    using namespace PlayerMask;
    if(attempted)return;
    attempted=true;
    if(REL::Module::get().version()!=SKSE::RUNTIME_VR_1_4_15) {
        SKSE::log::error("DAPA BODY MASK v4: this mesh-call map requires Skyrim VR 1.4.15; no hooks applied");return;
    }
    auto resources=AcquirePublishedBridgeResources();
    SKSE::log::info("DAPA BODY MASK v4: startup; bridge={} device={} rendererContext={}",
        static_cast<void*>(g_pBridge),static_cast<void*>(resources.d3dDevice),static_cast<void*>(resources.d3dContext));
    if(!g_pBridge || !resources.d3dDevice || !resources.d3dContext) {
        attempted=false; // Retry once scene resources are available at game load.
        SKSE::log::warn("DAPA BODY MASK v4: renderer resources not ready; will retry at game load");return;
    }
    // Use the exact renderer context rather than a possibly unwrapped context
    // returned by a device proxy. The draw filter must match Skyrim's caller.
    immediate=resources.d3dContext;immediate->AddRef();
    if(immediate->GetType()!=D3D11_DEVICE_CONTEXT_IMMEDIATE) {
        SKSE::log::error("DAPA BODY MASK v4: renderer context is deferred; body correction inactive");return;
    }
    if(!gpu.Initialize(resources.d3dDevice)) {
        SKSE::log::error("DAPA BODY MASK v4: GPU shader/state initialization failed; body correction inactive");return;
    }
    const auto imageBase=REL::Module::get().base();
    const auto callbackAddresses=Callbacks(std::make_index_sequence<DapaEngineDraw::sites.size()>{});
    // Validate every site BEFORE changing any executable instruction.
    for(size_t i=0;i<engineHooks.size();++i) {
        const auto& site=DapaEngineDraw::sites[i];
        if(!engineHooks[i].Prepare(site,reinterpret_cast<uint8_t*>(imageBase+site.rva))) {
            SKSE::log::error("DAPA BODY MASK v4: mesh call signature/chain rejected at RVA 0x{:X}, Win32={}; no engine patches applied",site.rva,engineHooks[i].error);return;
        }
    }
    // An opaque owner is never bypassed. Only the verified CSX owner has an
    // accepted-draw contract; a different owner must be integrated explicitly.
    for(size_t i=0;i<engineHooks.size();++i)if(engineHooks[i].chained) {
        auto* module=GetModuleHandleW(L"CommunityShaders.dll");
        if(i!=0 || !module || !csxAdapter.Prepare(reinterpret_cast<uintptr_t>(module),
               engineHooks[i].chained,reinterpret_cast<uintptr_t>(&CsxAccepted))) {
            SKSE::log::error("DAPA BODY MASK v4: existing draw owner not supported at RVA 0x{:X}; CSX acceptance signature failed, Win32={}; no patches applied",engineHooks[i].spec->rva,csxAdapter.error);
            return;
        }
        csxPrepared=true;
    }
    SKSE::AllocTrampoline(2048);
    for(size_t i=0;i<engineHooks.size();++i) {
        auto* stub=static_cast<uint8_t*>(SKSE::GetTrampoline().allocate(48));
        if(!engineHooks[i].Build(stub,callbackAddresses[i])) {
            SKSE::log::error("DAPA BODY MASK v4: trampoline failed at RVA 0x{:X}, Win32={}; no engine patches applied",engineHooks[i].spec->rva,engineHooks[i].error);return;
        }
    }
    // CommonLibVR BSShader declares SetupGeometry / RestoreGeometry at 6 / 7.
    if(!HookGeometry<0>(REL::VariantID(305261,255053,0x19050d0).address()) ||
       !HookGeometry<1>(REL::VariantID(305447,255194,0x1905f58).address())) {
        RollBack();return;
    }
    if(csxPrepared && !csxAdapter.Install()) {
        SKSE::log::error("DAPA BODY MASK v4: accepted-draw observer install failed, Win32={}",csxAdapter.error);
        RollBack();return;
    }
    for(auto& hook:engineHooks) {
        if(!hook.Install()) {
            SKSE::log::error("DAPA BODY MASK v4: engine call install failed at RVA 0x{:X}, Win32={}",hook.spec->rva,hook.error);RollBack();return;
        }
        SKSE::log::info("DAPA BODY MASK v4: stable mesh draw RVA 0x{:X}, existingOwner={}",hook.spec->rva,hook.chained);
    }
    ready.store(true,std::memory_order_release);
    SKSE::log::info("DAPA BODY MASK v4: 17 mesh sites + 4 geometry hooks + {} CSX accepted-draw observers installed; mask=depth-tested scene-sampled; no D3D11 vtable writes; awaiting coverage verification",csxPrepared?2:0);
}
