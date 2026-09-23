'use client'

import { FormEvent, useCallback, useEffect, useState } from 'react'
import { Archive, Download, LogOut, RefreshCw, ShieldAlert, UserRoundCog } from 'lucide-react'
import { Panel, StatusPill } from '@/components/app-shell'
import { client } from '@/lib/gateway'
import { CancelWorkspaceDeletion, CreateWorkspaceExport, DownloadWorkspaceExport, GetWorkspaceLifecycle, GetWorkspaceMembers, LeaveWorkspace, RequestWorkspaceDeletion, TransferWorkspaceOwnership, WorkspaceMemberInfo, WorkspaceLifecycleRequest, DataExportArtifact } from '@/lib/dtos'

const formatBytes=(value?:number)=>{const bytes=value??0;return bytes>=1024**2?`${(bytes/1024**2).toFixed(1)} MB`:bytes>=1024?`${(bytes/1024).toFixed(1)} KB`:`${bytes.toLocaleString()} B`}

export default function WorkspaceLifecycle({workspaceName,role}:{workspaceName?:string,role?:string}){
  const [requests,setRequests]=useState<WorkspaceLifecycleRequest[]>([]);const [exports,setExports]=useState<DataExportArtifact[]>([]);const [members,setMembers]=useState<WorkspaceMemberInfo[]>([]);const [exportExpiryDays,setExportExpiryDays]=useState(7);const [deletionDelayDays,setDeletionDelayDays]=useState(7)
  const [busy,setBusy]=useState<string>();const [notice,setNotice]=useState<{ok:boolean,text:string}>();const [target,setTarget]=useState('')
  const isOwner=role==='Owner';const isAdmin=isOwner||role==='Admin'
  const load=useCallback(async()=>{if(!isAdmin)return;const [life,team]=await Promise.all([client.api(new GetWorkspaceLifecycle()),client.api(new GetWorkspaceMembers())]);if(life.succeeded){setRequests(life.response?.results??[]);setExports(life.response?.exports??[]);setExportExpiryDays(life.response?.exportExpiryDays??7);setDeletionDelayDays(life.response?.workspaceDeletionDelayDays??7)}if(team.succeeded)setMembers((team.response?.results??[]).filter(x=>x.status==='Active'))},[isAdmin])
  useEffect(()=>{void load()},[load])
  const run=async(key:string,operation:()=>Promise<{succeeded:boolean,error?:{message?:string}}>,success:string)=>{setBusy(key);setNotice(undefined);const api=await operation();setNotice(api.succeeded?{ok:true,text:success}:{ok:false,text:api.error?.message??'Operation failed.'});setBusy(undefined);if(api.succeeded)await load()}
  const requestDeletion=(event:FormEvent<HTMLFormElement>)=>{event.preventDefault();const form=new FormData(event.currentTarget);const confirmation=String(form.get('confirmation')??'');void run('delete',()=>client.api(new RequestWorkspaceDeletion({confirmation})),'Organization deletion scheduled.')}
  const download=async(file:DataExportArtifact)=>{if(!file.id)return;try{const blob=await client.get(new DownloadWorkspaceExport({id:file.id}));const url=URL.createObjectURL(blob);const a=document.createElement('a');a.href=url;a.download=`${workspaceName||'organization'}-export.zip`;a.click();URL.revokeObjectURL(url)}catch(e){setNotice({ok:false,text:e instanceof Error?e.message:'Download failed.'})}}
  const deletion=requests.find(x=>x.type==='Delete'&&['Pending','Scheduled','Processing'].includes(x.status??''))
  return <div className="mt-5 space-y-5">
    {notice&&<Panel className={`p-4 text-sm ${notice.ok?'border-emerald-200 bg-emerald-50 text-emerald-700 dark:border-emerald-400/20 dark:bg-emerald-400/10 dark:text-emerald-200':'border-red-200 bg-red-50 text-red-700 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200'}`}>{notice.text}</Panel>}
    {!isAdmin&&<Panel className="p-6"><LogOut className="h-5 w-5 text-[#0b5cff]"/><h2 className="mt-4 font-semibold">Leave organization</h2><p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Remove your membership from this organization. Your personal account and memberships in other organizations are unchanged.</p><button disabled={!!busy} onClick={()=>void run('leave',()=>client.api(new LeaveWorkspace()),'You have left the organization.')} className="mt-5 inline-flex items-center gap-2 rounded-[10px] border border-slate-300 px-4 py-2.5 text-sm font-semibold dark:border-white/15"><LogOut className="h-4 w-4"/>Leave organization</button></Panel>}
    {isAdmin&&<div className="grid gap-5 xl:grid-cols-2"><Panel className="p-6"><div className="flex items-start gap-3"><span className="grid h-10 w-10 place-items-center rounded-xl bg-blue-50 text-[#0b5cff] dark:bg-blue-400/10"><Archive className="h-5 w-5"/></span><div><h2 className="font-semibold">Organization data export</h2><p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">Create a portable ZIP containing the organization’s data and stored files. The download expires and its stored archive is securely removed after {exportExpiryDays} days.</p></div></div><button onClick={()=>void run('export',()=>client.api(new CreateWorkspaceExport()),'Export requested. Background Jobs will prepare it shortly.')} disabled={!!busy} className="mt-5 inline-flex items-center gap-2 rounded-[10px] border border-slate-300 px-4 py-2.5 text-sm font-semibold dark:border-white/15"><RefreshCw className={`h-4 w-4 ${busy==='export'?'animate-spin':''}`}/>Prepare export</button>{exports.length>0&&<div className="mt-5 space-y-2">{exports.map(file=><button key={file.id} onClick={()=>void download(file)} className="flex w-full items-start justify-between gap-3 rounded-xl bg-slate-50 p-3 text-left dark:bg-white/[.035]"><span className="min-w-0"><strong className="block text-xs text-emerald-700 dark:text-emerald-300">Ready to download</strong><span className="mt-1 block text-[10px] text-slate-400">{formatBytes(file.byteLength)} · Expires {file.expiresAt?new Date(file.expiresAt).toLocaleString():'—'}</span><span className="mt-1 block break-all font-mono text-[9px] leading-4 text-slate-500 dark:text-slate-400">SHA-256 {file.sha256||'—'}</span></span><Download className="mt-1 h-4 w-4 shrink-0 text-[#0b5cff]"/></button>)}</div>}</Panel>
      <Panel className="p-6">
        <UserRoundCog className="h-5 w-5 text-[#0b5cff]"/>
        <h2 className="mt-4 font-semibold">Ownership</h2>
        <p className="mt-2 text-sm leading-6 text-slate-500 dark:text-slate-400">Transfer ownership to an active organization member before the current owner leaves.</p>
        {isOwner ? <div className="mt-5 flex gap-2">
          <select aria-label="New organization owner" value={target} onChange={e=>setTarget(e.target.value)} disabled={!members.some(x=>x.userId&&x.role!=='Owner')} className="min-w-0 flex-1 rounded-[10px] border-slate-200 bg-white text-sm disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/10 dark:bg-[#0c1729]">
            <option value="">{members.some(x=>x.userId&&x.role!=='Owner') ? 'Select member' : 'No eligible members'}</option>
            {members.filter(x=>x.userId&&x.role!=='Owner').map(x=><option key={x.id} value={x.userId}>{x.displayName&&x.email?`${x.displayName} (${x.email})`:x.displayName||x.email||'Member'}</option>)}
          </select>
          <button disabled={!target||!!busy} onClick={()=>void run('transfer',()=>client.api(new TransferWorkspaceOwnership({targetUserId:target})),'Ownership transferred.')} className="rounded-[10px] bg-[#0b5cff] px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-50">Transfer</button>
        </div> : <button disabled={!!busy} onClick={()=>void run('leave',()=>client.api(new LeaveWorkspace()),'You have left the organization.')} className="mt-5 inline-flex items-center gap-2 rounded-[10px] border border-slate-300 px-4 py-2.5 text-sm font-semibold dark:border-white/15"><LogOut className="h-4 w-4"/>Leave organization</button>}
      </Panel></div>}
    {isOwner&&<Panel className="border-red-200 p-6 dark:border-red-400/20">
      <div className="flex items-start gap-3">
        <span className="grid h-10 w-10 shrink-0 place-items-center rounded-xl bg-red-50 text-red-600 dark:bg-red-400/10"><ShieldAlert className="h-5 w-5"/></span>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="font-semibold">Delete organization</h2>
              <p className="mt-1 text-xs leading-5 text-slate-500 dark:text-slate-400">Permanently deletes this organization’s stored files, usage data, memberships, notifications, exports, and configuration after a {deletionDelayDays}-day cancellation period.</p>
            </div>
            {deletion&&<StatusPill tone="amber">Scheduled {deletion.scheduledAt?new Date(deletion.scheduledAt).toLocaleString():''}</StatusPill>}
          </div>
          <div className="mt-4 rounded-xl border border-red-200 bg-red-50/70 p-4 text-xs leading-5 text-red-800 dark:border-red-400/20 dark:bg-red-400/10 dark:text-red-200">
            <strong className="block">User accounts are not deleted.</strong>
            <span>Members keep their personal accounts and access to any other organizations. All members lose access to this organization when deletion completes. An active paid subscription must be canceled from Billing before deletion can be scheduled.</span>
          </div>
          {deletion ? <div className="mt-5">
            <p className="text-xs leading-5 text-slate-500 dark:text-slate-400">The organization is read-only until the scheduled deletion. You can cancel this request before processing begins.</p>
            <button disabled={!!busy} onClick={()=>void run('cancel',()=>client.api(new CancelWorkspaceDeletion()),'Organization deletion canceled.')} className="mt-3 rounded-[10px] border border-slate-300 px-4 py-2.5 text-sm font-semibold dark:border-white/15">Cancel deletion</button>
          </div> : <form onSubmit={requestDeletion} className="mt-5 flex flex-col gap-2 sm:flex-row">
            <input aria-label="Organization name confirmation" name="confirmation" required placeholder={`Type “${workspaceName}” to confirm`} className="min-w-0 flex-1 rounded-[10px] border-red-200 bg-white text-sm dark:border-red-400/20 dark:bg-white/5"/>
            <button type="submit" disabled={!!busy} className="rounded-[10px] bg-red-600 px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-40">Schedule deletion</button>
          </form>}
        </div>
      </div>
    </Panel>}
  </div>
}
