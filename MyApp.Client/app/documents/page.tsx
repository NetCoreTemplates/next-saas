'use client'

import { ChangeEvent, DragEvent, useCallback, useEffect, useRef, useState } from 'react'
import { CircleAlert, CloudUpload, Download, FileCheck2, FileText, HardDrive, Loader2, Search, Trash2, X } from 'lucide-react'
import AppShell, { PageHeading, Panel, StatusPill } from '@/components/app-shell'
import FeatureGate from '@/components/feature-gate'
import { ValidateAuth } from '@/lib/auth'
import { client } from '@/lib/gateway'
import { DeleteStoredFile, DownloadStoredFile, QueryStoredFiles, StoredFileInfo, UploadStoredFile } from '@/lib/dtos'
import { LoadingPanel, useSaasDashboard } from '@/lib/use-saas'

type UploadEntry = { name: string, size: number, status: 'pending'|'uploading'|'done'|'error', error?: string }

const bytes = (value?: number) => {
  const n=value ?? 0
  if (n < 1024) return `${n} B`
  if (n < 1024 ** 2) return `${(n / 1024).toFixed(1)} KB`
  if (n < 1024 ** 3) return `${(n / 1024 ** 2).toFixed(1)} MB`
  return `${(n / 1024 ** 3).toFixed(1)} GB`
}

function DocumentsPage() {
  const {data,loading:dashboardLoading,refresh:refreshDashboard}=useSaasDashboard()
  const [files,setFiles]=useState<StoredFileInfo[]>([])
  const [loading,setLoading]=useState(true)
  const [busy,setBusy]=useState(false)
  const [search,setSearch]=useState('')
  const [notice,setNotice]=useState<{ok:boolean,text:string}>()
  const [queue,setQueue]=useState<UploadEntry[]>([])
  const [dragging,setDragging]=useState(false)
  const fileInput=useRef<HTMLInputElement>(null)

  const readOnly=data?.subscription?.accessMode && data.subscription.accessMode!=='Full' && data.subscription.accessMode!=='Grace'

  const load=useCallback(async(q='')=>{
    setLoading(true)
    const api=await client.api(new QueryStoredFiles({search:q||undefined,take:100}))
    if(api.succeeded)setFiles(api.response?.results ?? [])
    else setNotice({ok:false,text:api.error?.message ?? 'Unable to load files.'})
    setLoading(false)
  },[])
  useEffect(()=>{void load()},[load])

  // Each file is its own idempotent upload, so it carries its own key, reservation, and
  // quota decision. One rejected file therefore never discards the files around it.
  const uploadFiles=useCallback(async(selection:FileList|File[])=>{
    const picked=Array.from(selection)
    if(!picked.length)return
    setBusy(true);setNotice(undefined)
    setQueue(picked.map(file=>({name:file.name,size:file.size,status:'pending'})))
    let uploaded=0
    for(const [index,file] of picked.entries()){
      setQueue(current=>current.map((entry,i)=>i===index?{...entry,status:'uploading'}:entry))
      const form=new FormData();form.append('file',file)
      const api=await client.apiForm(new UploadStoredFile({idempotencyKey:crypto.randomUUID()}),form)
      if(api.succeeded)uploaded++
      setQueue(current=>current.map((entry,i)=>i===index?api.succeeded?{...entry,status:'done'}:{...entry,status:'error',error:api.error?.message ?? 'Upload failed.'}:entry))
    }
    const failed=picked.length-uploaded
    setNotice(failed===0
      ?{ok:true,text:`${uploaded} ${uploaded===1?'file is':'files are'} securely stored and counted against your organization’s quota.`}
      :uploaded===0
        ?{ok:false,text:`${failed===1?'The file was':`All ${failed} files were`} rejected. Each reason is listed below.`}
        :{ok:false,text:`${uploaded} of ${picked.length} files uploaded. ${failed===1?'One file was':`${failed} files were`} rejected; each reason is listed below.`})
    if(uploaded>0){
      await Promise.all([load(search),refreshDashboard()])
      // Keep only what still needs the developer's attention.
      setQueue(current=>current.filter(entry=>entry.status==='error'))
    }
    setBusy(false)
  },[load,refreshDashboard,search])

  const upload=async(event:ChangeEvent<HTMLInputElement>)=>{
    await uploadFiles(event.target.files ?? [])
    if(fileInput.current)fileInput.current.value=''
  }

  const drop=async(event:DragEvent<HTMLDivElement>)=>{
    event.preventDefault();setDragging(false)
    if(busy||readOnly)return
    await uploadFiles(event.dataTransfer.files)
  }

  const remove=async(file:StoredFileInfo)=>{
    if(!file.id || !confirm(`Delete ${file.name}?`))return
    const api=await client.api(new DeleteStoredFile({id:file.id}))
    setNotice(api.succeeded?{ok:true,text:`${file.name} is queued for secure deletion.`}:{ok:false,text:api.error?.message ?? 'Delete failed.'})
    if(api.succeeded){setFiles(current=>current.filter(x=>x.id!==file.id));void refreshDashboard()}
  }

  const download=async(file:StoredFileInfo)=>{
    if(!file.id)return
    try {
      const blob=await client.get(new DownloadStoredFile({id:file.id}))
      const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download=file.name ?? 'download';a.click();URL.revokeObjectURL(url)
    } catch (e) { setNotice({ok:false,text:e instanceof Error?e.message:'Download failed.'}) }
  }

  if(dashboardLoading)return <AppShell><LoadingPanel/></AppShell>
  const documentUsage=data?.usage?.find(x=>x.meterKey==='documents.stored')
  const storageUsage=data?.usage?.find(x=>x.meterKey==='storage.bytes')
  return <AppShell workspaceName={data?.workspace?.name}>
    <PageHeading eyebrow="Secure document storage" title="Documents" description="A deliberately simple file workflow that demonstrates secure tenant isolation, reservation-based quotas, streaming storage, analytics, and auditable deletion." action={<label className={`inline-flex cursor-pointer items-center gap-2 rounded-[10px] px-4 py-2.5 text-sm font-semibold text-white ${busy||readOnly?'pointer-events-none bg-slate-400':'bg-[#0b5cff]'}`}><CloudUpload className="h-4 w-4"/>{busy?'Uploading…':'Upload documents'}<input ref={fileInput} type="file" multiple onChange={upload} className="hidden" disabled={busy||!!readOnly}/></label>}/>
    {readOnly&&<Panel className="mb-5 border-amber-200 bg-amber-50 p-4 text-sm text-amber-800 dark:border-amber-400/20 dark:bg-amber-400/10 dark:text-amber-200"><strong>{data?.subscription?.accessMode} access.</strong> {data?.subscription?.accessReason || 'Uploads are temporarily disabled; existing files remain available.'}</Panel>}
    {notice&&<Panel className={`mb-5 p-4 text-sm ${notice.ok?'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-400/20 dark:bg-emerald-400/10 dark:text-emerald-200':'border-red-200 bg-red-50 text-red-700 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200'}`}>{notice.text}</Panel>}
    {queue.length>0&&<Panel className="mb-5 overflow-hidden">
      <div className="flex items-center justify-between border-b border-slate-200 px-6 py-4 dark:border-white/10">
        <h2 className="text-sm font-semibold">{busy?`Uploading ${Math.min(queue.filter(x=>x.status!=='pending').length||1,queue.length)} of ${queue.length}`:`${queue.length} ${queue.length===1?'file needs':'files need'} attention`}</h2>
        {!busy&&<button onClick={()=>setQueue([])} className="grid h-8 w-8 place-items-center rounded-lg text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-white/5" title="Dismiss"><X className="h-4 w-4"/></button>}
      </div>
      <ul data-testid="upload-queue" className="divide-y divide-slate-100 dark:divide-white/5">{queue.map((entry,i)=><li key={`${entry.name}-${i}`} className="flex items-center gap-3 px-6 py-3 text-sm">
        <span className="grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-slate-100 text-slate-500 dark:bg-white/5">
          {entry.status==='uploading'?<Loader2 className="h-4 w-4 animate-spin text-[#0b5cff]"/>:entry.status==='error'?<CircleAlert className="h-4 w-4 text-red-500"/>:entry.status==='done'?<FileCheck2 className="h-4 w-4 text-emerald-500"/>:<FileText className="h-4 w-4"/>}
        </span>
        <div className="min-w-0 flex-1">
          <p className="truncate font-medium">{entry.name}</p>
          <p className="mt-0.5 truncate text-xs text-slate-400">{entry.status==='error'?entry.error:`${bytes(entry.size)} · ${entry.status==='uploading'?'uploading':entry.status==='done'?'stored':'queued'}`}</p>
        </div>
        <StatusPill tone={entry.status==='error'?'red':entry.status==='done'?'green':'slate'}>{entry.status==='error'?'Rejected':entry.status==='done'?'Stored':entry.status==='uploading'?'Uploading':'Queued'}</StatusPill>
      </li>)}</ul>
    </Panel>}
    <FeatureGate feature="files.basic" entitlements={data?.entitlements}>
      <div className="mb-5 grid gap-4 md:grid-cols-2">
        {[{icon:FileCheck2,label:'Documents stored',usage:documentUsage,format:(n?:number)=>(n??0).toLocaleString()},{icon:HardDrive,label:'Storage used',usage:storageUsage,format:bytes}].map(({icon:Icon,label,usage,format})=><Panel key={label} className="p-5"><div className="flex items-center justify-between"><span className="grid h-10 w-10 place-items-center rounded-xl bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><Icon className="h-5 w-5"/></span><StatusPill tone={(usage?.percentUsed??0)>=80?'amber':'green'}>{usage?.kind ?? 'Gauge'}</StatusPill></div><div className="mt-5 flex items-end justify-between"><div><p className="text-2xl font-semibold tracking-[-.04em]">{format(usage?.usedUnits)}</p><p className="mt-1 text-xs text-slate-400">of {usage?.allowance==null?'unlimited':format(usage.allowance)}</p></div><span className="text-sm font-semibold text-[#0b5cff]">{usage?.percentUsed ?? 0}%</span></div><div className="mt-4 h-2 overflow-hidden rounded-full bg-slate-100 dark:bg-white/10"><div className="h-full rounded-full bg-gradient-to-r from-[#0b5cff] to-[#86efcd]" style={{width:`${Math.min(100,Math.max(0,usage?.percentUsed??0))}%`}}/></div></Panel>)}
      </div>
      <div onDragOver={(e:DragEvent<HTMLDivElement>)=>{e.preventDefault();if(!busy&&!readOnly)setDragging(true)}} onDragLeave={()=>setDragging(false)} onDrop={drop} data-testid="drop-zone">
      <Panel className={`overflow-hidden transition ${dragging?'border-[#0b5cff] ring-2 ring-[#0b5cff]/20':''}`}><div className="flex flex-col gap-4 border-b border-slate-200 px-6 py-5 sm:flex-row sm:items-center sm:justify-between dark:border-white/10"><div><h2 className="font-semibold">Organization files</h2><p className="mt-1 text-xs text-slate-400">Available to members of this organization; original names are preserved as metadata. Drop files here to upload them.</p></div><label className="relative"><Search className="absolute left-3 top-2.5 h-4 w-4 text-slate-400"/><input value={search} onChange={e=>setSearch(e.target.value)} onKeyDown={e=>{if(e.key==='Enter')void load(search)}} placeholder="Search files" className="w-full rounded-[10px] border-slate-200 bg-white py-2 pl-9 pr-3 text-sm dark:border-white/10 dark:bg-white/5 sm:w-64"/></label></div>
        {loading?<div className="grid min-h-52 place-items-center"><span className="h-7 w-7 animate-spin rounded-full border-2 border-blue-100 border-t-[#0b5cff]"/></div>:files.length?<div className="divide-y divide-slate-100 dark:divide-white/5">{files.map(file=><div key={file.id} className="flex items-center gap-4 px-6 py-4"><span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-slate-100 text-slate-500 dark:bg-white/5"><FileText className="h-5 w-5"/></span><div className="min-w-0 flex-1"><p className="truncate text-sm font-semibold">{file.name}</p><p className="mt-1 text-xs text-slate-400">{bytes(file.byteLength)} · {file.createdDate?new Date(file.createdDate).toLocaleString():'—'} · <span className="font-mono">{file.sha256?.slice(0,12)}…</span></p></div><StatusPill tone={file.status==='Available'?'green':'slate'}>{file.status}</StatusPill><button onClick={()=>void download(file)} className="grid h-9 w-9 place-items-center rounded-lg text-slate-400 hover:bg-blue-50 hover:text-[#0b5cff] dark:hover:bg-blue-400/10" title="Download"><Download className="h-4 w-4"/></button><button onClick={()=>void remove(file)} className="grid h-9 w-9 place-items-center rounded-lg text-slate-400 hover:bg-red-50 hover:text-red-600 dark:hover:bg-red-400/10" title="Delete"><Trash2 className="h-4 w-4"/></button></div>)}</div>:<div className="grid min-h-64 place-items-center p-8 text-center"><div><CloudUpload className="mx-auto h-8 w-8 text-slate-300"/><h3 className="mt-4 font-semibold">Your document library is ready</h3><p className="mt-2 text-sm text-slate-500">Drop files here, or upload several at once, to exercise storage and document-count quotas.</p></div></div>}
      </Panel>
      </div>
    </FeatureGate>
  </AppShell>
}
export default ValidateAuth(DocumentsPage)
