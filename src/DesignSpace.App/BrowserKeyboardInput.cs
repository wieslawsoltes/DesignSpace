using DesignSpace.Controls.Uno;
using Windows.System;
namespace DesignSpace.App;

internal static class BrowserKeyboardInput
{
    public static void Initialize()
    {
#if __WASM__
        Uno.Foundation.WebAssemblyRuntime.InvokeJS("""
            (() => {
              if(globalThis.DesignSpaceKeys) return 'ready';
              const state=globalThis.DesignSpaceKeys={control:false,shift:false,alt:false,meta:false,space:false};
              const update=e=>{ if(!e.isTrusted) return; state.control=!!e.ctrlKey; state.shift=!!e.shiftKey; state.alt=!!e.altKey; state.meta=!!e.metaKey; };
              window.addEventListener('keydown',e=>{
                update(e); if(e.isTrusted && e.code==='Space') state.space=true;
                const typing=/^(INPUT|TEXTAREA)$/.test(e.target?.tagName || '') || e.target?.isContentEditable;
                if(e.key==='F6' || (e.ctrlKey && e.key==='F4')) e.preventDefault();
                if((e.ctrlKey || e.metaKey) && (e.key.toLowerCase()==='s' || (!typing && ['n','o','d','g'].includes(e.key.toLowerCase())))) e.preventDefault();
              },true);
              window.addEventListener('keyup',e=>{update(e); if(e.isTrusted && e.code==='Space') state.space=false;},true);
              window.addEventListener('pointerdown',update,true); window.addEventListener('pointermove',update,true);
              window.addEventListener('wheel',update,{capture:true,passive:true});
              const reset=()=>Object.keys(state).forEach(k=>state[k]=false);
              window.addEventListener('blur',reset); document.addEventListener('visibilitychange',()=>{if(document.hidden) reset();});
              return 'ready';
            })()
            """);
        // With accessibility opt-in still visible, Uno intentionally lets the first
        // browser Tab from <body> reach that entry point. A managed canvas-focus
        // proxy alone does not change DOM focus. Explicit artboard interaction must
        // focus the real host as well; do not remove/disable the accessibility entry
        // point, or steal focus from semantic elements when accessibility is enabled.
        DesignerKeys.HostFocusOverride=()=>Uno.Foundation.WebAssemblyRuntime.InvokeJS("""
            (() => {
              if (!document.getElementById('uno-enable-accessibility')) return 'semantic-focus';
              const host = document.getElementById('uno-body');
              if (!host) return 'no-host';
              if (!host.hasAttribute('tabindex')) host.setAttribute('tabindex', '-1');
              host.focus({preventScroll:true});
              return 'host-focus';
            })()
            """);
        DesignerKeys.StateOverride=key=>
        {
            var name=key switch { VirtualKey.Control=>"control",VirtualKey.Shift=>"shift",VirtualKey.Menu=>"alt",VirtualKey.LeftWindows or VirtualKey.RightWindows=>"meta",VirtualKey.Space=>"space",_=>null };
            return name is null ? null : Uno.Foundation.WebAssemblyRuntime.InvokeJS("globalThis.DesignSpaceKeys."+name+" ? 'down' : 'up'")=="down";
        };
#endif
    }
}
