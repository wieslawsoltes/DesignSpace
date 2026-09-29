#if __WASM__
using System.Text.Json;
using Uno.Foundation;
namespace DesignSpace.App;

/// <summary>Browser upload/copy provider. DOM selection and file reads remain on the page's JS realm.</summary>
internal static class BrowserFileUpload
{
    internal sealed record SelectedFile(string Name,string? Text,byte[]? Bytes);
    private static bool _pending;
    public static async Task<SelectedFile?> PickAsync(bool image)
    {
        if(_pending)throw new InvalidOperationException("A file selection is already open.");
        _pending=true;var id=Guid.NewGuid().ToString("N");var key=JsonSerializer.Serialize(id);
        const string start="""
            ((id,image) => {
              const requests=globalThis.__designSpaceUploads ??= Object.create(null);
              const input=document.createElement('input');input.type='file';input.style.display='none';
              input.accept=image ? '.png,.jpg,.jpeg,.webp,.gif' : '.xaml,.designspace,.designspace-workspace,.json';
              input.dataset.designspaceUpload=id;
              const entry={status:'pending',input};requests[id]=entry;
              const finish=value=>{if(entry.status!=='pending'||requests[id]!==entry)return;entry.status=value.status;input.remove();requests[id]=value;};
              input.addEventListener('cancel',()=>finish({status:'cancelled'}),{once:true});
              input.addEventListener('change',async()=>{
                const file=input.files?.[0];if(!file){finish({status:'cancelled'});return;}
                const name=file.name,limit=image?4*1024*1024:name.toLowerCase().endsWith('.designspace-workspace')?16*1024*1024:8*1024*1024;
                if(file.size>limit){finish({status:'error',message:'Import exceeds the file size limit.'});return;}
                try {
                  const bytes=new Uint8Array(await file.arrayBuffer());
                  if(image){
                    const chunks=[];for(let i=0;i<bytes.length;i+=32768)chunks.push(String.fromCharCode(...bytes.subarray(i,i+32768)));
                    finish({status:'ready',name,data:btoa(chunks.join(''))});
                  } else {
                    let encoding='utf-8',offset=0;
                    if(bytes[0]===255&&bytes[1]===254){encoding='utf-16le';offset=2;}
                    else if(bytes[0]===254&&bytes[1]===255){encoding='utf-16be';offset=2;}
                    finish({status:'ready',name,text:new TextDecoder(encoding,{fatal:true}).decode(bytes.subarray(offset))});
                  }
                } catch(error){finish({status:'error',message:String(error.message??error)});}
              },{once:true});
              document.body.append(input);
              try{input.click();}catch(error){finish({status:'error',message:String(error.message??error)});}
              return 'opened';
            })
            """;
        try
        {
            // No cross-reload JS promise/worker handle crosses into .NET. Only bounded data is copied.
            WebAssemblyRuntime.InvokeJS(start+"("+key+","+(image?"true":"false")+")");
            var clock=System.Diagnostics.Stopwatch.StartNew();
            while(clock.Elapsed<TimeSpan.FromMinutes(10))
            {
                var json=WebAssemblyRuntime.InvokeJS("JSON.stringify((()=>{const r=globalThis.__designSpaceUploads?.["+key+"];return r?.status==='pending'?{status:'pending'}:r??{status:'cancelled'};})())");
                using var result=JsonDocument.Parse(json);var value=result.RootElement;
                switch(value.GetProperty("status").GetString())
                {
                    case "cancelled":return null;
                    case "error":throw new IOException(value.GetProperty("message").GetString());
                    case "ready":
                        var name=value.GetProperty("name").GetString()??"Upload";
                        return image?new(name,null,Convert.FromBase64String(value.GetProperty("data").GetString()!)):new(name,value.GetProperty("text").GetString(),null);
                }
                await Task.Delay(50);
            }
            throw new IOException("File selection timed out. Open the picker again.");
        }
        finally
        {
            try{WebAssemblyRuntime.InvokeJS("(()=>{const r=globalThis.__designSpaceUploads?.["+key+"];r?.input?.remove();if(globalThis.__designSpaceUploads)delete globalThis.__designSpaceUploads["+key+"];return 'closed';})()");}
            finally{_pending=false;}
        }
    }
}
#endif
