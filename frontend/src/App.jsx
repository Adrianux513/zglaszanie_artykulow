import React, {useState, useEffect} from 'react'
import axios from 'axios'

// Cache utilities
const cache = {
  get: (key) => {
    try {
      const item = localStorage.getItem(`cache_${key}`);
      if (!item) return null;
      const parsed = JSON.parse(item);
      if (parsed.expires < Date.now()) {
        localStorage.removeItem(`cache_${key}`);
        return null;
      }
      return parsed.data;
    } catch (e) {
      return null;
    }
  },
  set: (key, data, minutes = 5) => {
    try {
      localStorage.setItem(`cache_${key}`, JSON.stringify({
        data,
        expires: Date.now() + minutes * 60 * 1000
      }));
    } catch (e) {
      console.error('Cache set error:', e);
    }
  },
  remove: (key) => {
    localStorage.removeItem(`cache_${key}`);
  },
  removePattern: (pattern) => {
    Object.keys(localStorage).forEach(key => {
      if (key.startsWith('cache_') && key.includes(pattern)) {
        localStorage.removeItem(key);
      }
    });
  }
};

// Cached API call
const apiGet = async (url, options = {}) => {
  const cacheKey = url.replace(/[^a-zA-Z0-9_]/g, '_');
  const cached = cache.get(cacheKey);
  if (cached) {
    console.log('Cache hit:', url);
    return cached;
  }

  const response = await axios.get(url, options);
  cache.set(cacheKey, response.data, 5);
  return response.data;
};

const apiPost = async (url, data, options = {}) => {
  const response = await axios.post(url, data, options);
  // Invalidate related caches
  cache.removePattern('submissions');
  cache.removePattern('articles');
  cache.removePattern('reviews');
  return response.data;
};

const apiPut = async (url, data, options = {}) => {
  const response = await axios.put(url, data, options);
  cache.removePattern('submissions');
  cache.removePattern('articles');
  return response.data;
};

const apiDelete = async (url, options = {}) => {
  const response = await axios.delete(url, options);
  cache.removePattern('submissions');
  cache.removePattern('articles');
  return response.data;
};

export default function App(){
  const [token, setToken] = useState(localStorage.getItem('token'))
  const [user, setUser] = useState(JSON.parse(localStorage.getItem('user') || 'null'))
  const [viewPublic, setViewPublic] = useState(false)

  useEffect(()=>{
    if(token && user===null){
      const stored = localStorage.getItem('user')
      if(stored) setUser(JSON.parse(stored))
    }
  },[token])

  if(!token) return viewPublic
    ? <PublicArticles onBack={()=>setViewPublic(false)} />
    : <Auth onLogin={(t,u)=>{
        cache.removePattern('');  // Clear all cache on login
        localStorage.setItem('token',t); 
        localStorage.setItem('user', JSON.stringify(u)); 
        setToken(t); 
        setUser(u)
      }} onBrowsePublic={()=>setViewPublic(true)} />

  return <div className="container">
    <h1>System zgłoszeń (student project)</h1>
    <Dashboard token={token} user={user} onLogout={()=>{
      cache.removePattern('');  // Clear all cache on logout
      localStorage.removeItem('token'); 
      localStorage.removeItem('user'); 
      setToken(null); 
      setUser(null)
    }} />
  </div>
}

function Auth({onLogin, onBrowsePublic}){
  const [email,setEmail]=useState('student@example.com')
  const [pass,setPass]=useState('pass123')
  const [registerMode,setRegisterMode]=useState(false)
  const [name,setName]=useState('')

  const login = async ()=>{
    try{
      const res = await apiPost('http://localhost:5000/api/auth/login',{email, password: pass})
      onLogin(res.token, res.user)
    }catch(e){
      alert('Login failed')
      console.error(e)
    }
  }

  const register = async ()=>{
    try{
      await apiPost('http://localhost:5000/api/auth/register',{email, password: pass, name})
      alert('Registered -- now login')
      setRegisterMode(false)
    }catch(e){ alert('Register failed'); console.error(e) }
  }

  return <div style={{maxWidth:480, margin:'3rem auto'}} className="card">
    <h2>{registerMode ? 'Register' : 'Login'}</h2>
    <form onSubmit={e=>{ e.preventDefault(); registerMode ? register() : login(); }}>
      {registerMode && <input placeholder="Name" value={name} onChange={e=>setName(e.target.value)} />}
      <input placeholder="Email" value={email} onChange={e=>setEmail(e.target.value)} />
      <input placeholder="Password" type="password" value={pass} onChange={e=>setPass(e.target.value)} />
      <div style={{display:'flex',gap:8, marginTop:8}}>
        {!registerMode ? <button type="submit">Login</button> : <button type="submit">Register</button>}
        <button type="button" onClick={()=>setRegisterMode(!registerMode)} style={{background:'#6b7280'}}>Switch</button>
        {!registerMode && <button type="button" onClick={onBrowsePublic} style={{background:'#2563eb', color:'white'}}>Obejrzyj artykuły</button>}
      </div>
    </form>
    <div style={{marginTop:8,fontSize:13,color:'#555'}}>Test accounts: admin@example.com/adminpass (admin), student@example.com/pass123 (author), reviewer1@example.com/reviewerpass1 (reviewer), reviewer2@example.com/reviewerpass2 (reviewer), reviewer3@example.com/reviewerpass3 (reviewer)</div>
  </div>
}

function PublicArticles({onBack}){
  const [articles,setArticles]=useState([])
  const [loading,setLoading]=useState(false)
  const [filters,setFilters]=useState({title:'',authors:'',keywords:'',status:'published',category:''})

  const fetchArticles = async () => {
    setLoading(true)
    try{
      const res = await apiGet('http://localhost:5000/api/articles?title='+encodeURIComponent(filters.title)+'&authors='+encodeURIComponent(filters.authors)+'&keywords='+encodeURIComponent(filters.keywords)+'&category='+encodeURIComponent(filters.category))
      setArticles(res.articles || [])
    }catch(e){
      console.error(e)
      alert('Load failed')
    }finally{setLoading(false)}
  }

  useEffect(()=>{ fetchArticles() },[])

  const applyFilter = (field,value) => setFilters(prev => ({...prev,[field]:value}))

  const statusOptions = [
    { value:'', label:'Wszystkie' },
    { value:'draft', label:'Wersja robocza' },
    { value:'submitted', label:'Zgłoszony' },
    { value:'in review', label:'W recenzji' },
    { value:'accepted', label:'Zaakceptowany' },
    { value:'rejected', label:'Odrzucony' },
    { value:'published', label:'Opublikowany' }
  ]

  const exportList = (format) => {
    const params = new URLSearchParams(filters)
    window.open(`http://localhost:5000/api/articles/export/${format}?${params.toString()}`, '_blank')
  }

  return <div className="card">
    <button onClick={onBack} style={{marginBottom:12}}>Powrót</button>
    <h3>Przeglądaj artykuły</h3>
    <div style={{display:'grid', gap:8, marginBottom:12}}>
      <input placeholder="Tytuł" value={filters.title} onChange={e=>applyFilter('title', e.target.value)} />
      <input placeholder="Autorzy" value={filters.authors} onChange={e=>applyFilter('authors', e.target.value)} />
      <input placeholder="Słowa kluczowe" value={filters.keywords} onChange={e=>applyFilter('keywords', e.target.value)} />
      <input placeholder="Dział naukowy" value={filters.category} onChange={e=>applyFilter('category', e.target.value)} />
      <select value={filters.status} onChange={e=>applyFilter('status', e.target.value)}>
        {statusOptions.map(opt => <option key={opt.value} value={opt.value}>{opt.label}</option>)}
      </select>
      <button onClick={fetchArticles} style={{background:'#2563eb', color:'white'}}>Filtruj</button>
      <div style={{display:'flex', gap:8}}>
        <button onClick={()=>exportList('csv')} style={{background:'#10b981'}}>Eksportuj CSV</button>
        <button onClick={()=>exportList('pdf')} style={{background:'#f59e0b'}}>Eksportuj PDF</button>
      </div>
    </div>
    {loading ? <div>Loading...</div> :
      articles.length===0 ? <div>Brak artykułów</div> :
      articles.map(a => <div key={a.id} className="card" style={{marginBottom:8}}>
        <strong>{a.title}</strong>
        <div style={{fontSize:13,color:'#555'}}>{a.abstract}</div>
        <div style={{fontSize:12,color:'#777',marginTop:6}}>
          Autorzy: {a.authors || '—'}<br />
          Dział: {a.category || '—'}<br />
          Słowa kluczowe: {(a.keywords || '').replace(/;/g, ', ')}<br />
          Status: {statusLabel(a.status)} — Dodano: {new Date(a.createdAt).toLocaleString()}
        </div>
        {a.files && a.files.length > 0 && <div style={{marginTop:8}}>
          <strong>Pliki:</strong>
          {a.files.map(file => <div key={file}><a href={`http://localhost:5000/api/articles/${a.id}/files/${encodeURIComponent(file)}`} target="_blank" rel="noreferrer">Otwórz {file}</a></div>)}
        </div>}
        <div style={{marginTop:8}}>
          <a href={`http://localhost:5000/api/articles/${a.id}/pdf`} target="_blank" rel="noreferrer" style={{background:'#2563eb', color:'white', padding:'8px 12px', borderRadius:'4px', textDecoration:'none', fontSize:'12px'}}>Pobierz PDF</a>
        </div>
      </div>)
    }
  </div>
}

function Dashboard({token, user, onLogout}){
  const [tab, setTab] = useState('new')
  return <div>
    <div className="nav">
      <button onClick={()=>setTab('new')}>New submission</button>
      <button onClick={()=>setTab('mine')}>My submissions</button>
      {user?.role === 'reviewer' && <button onClick={()=>setTab('assigned')}>Assigned reviews</button>}
      <button onClick={()=>setTab('notifications')}>Notifications</button>
      {user?.role === 'admin' && <button onClick={()=>setTab('admin')}>Admin: all submissions</button>}
      {user?.role === 'admin' && <button onClick={()=>setTab('stats')}>Statistics</button>}
      <button onClick={onLogout} style={{background:'#ef4444'}}>Logout</button>
    </div>
    {tab==='new' && <NewSubmission token={token} />}
    {tab==='mine' && <MySubmissions token={token} />}
    {tab==='assigned' && <AssignedReviews token={token} />}
    {tab==='notifications' && <Notifications token={token} />}
    {tab==='admin' && <AdminSubmissions token={token} />}
    {tab==='stats' && <Statistics token={token} />}
  </div>
}

function statusLabel(status){
  switch(status){
    case 'draft': return 'w wersji roboczej';
    case 'submitted': return 'zgłoszony';
    case 'in review': return 'w recenzji';
    case 'accepted': return 'zaakceptowany';
    case 'rejected': return 'odrzucony';
    case 'published': return 'opublikowany';
    default: return status || 'nieznany';
  }
}

function NewSubmission({token}){
  const [title,setTitle]=useState('')
  const [authors,setAuthors]=useState('')
  const [category,setCategory]=useState('')
  const [abstract,setAbstract]=useState('')
  const [keywordsText,setKeywordsText]=useState('')
  const [file,setFile]=useState(null)
  const [currentFiles,setCurrentFiles]=useState([])
  const [submissionId,setSubmissionId]=useState(null)
  const [loading,setLoading]=useState(false)
  const [message,setMessage]=useState('')

  const ensureDraft = async ()=>{
    if (submissionId) return submissionId
    const keywords = keywordsText.split(',').map(k=>k.trim()).filter(Boolean)
    const res = await apiPost('http://localhost:5000/api/submissions',{ 
      title,
      abstract,
      authors,
      category,
      keywords
    },{headers:{Authorization:'Bearer '+token}})
    setSubmissionId(res.submission.id)
    setMessage('Draft zapisany. Możesz dodawać pliki lub wysłać zgłoszenie do recenzji.')
    return res.submission.id
  }

  const createDraft = async ()=>{
    setLoading(true)
    try{
      await ensureDraft()
    }catch(e){
      console.error(e)
      alert('Error: ' + (e.response?.data?.error || e.response?.status || e.message))
    }finally{ setLoading(false) }
  }

  const uploadFile = async ()=>{
    if (!file) return alert('Select a file first')
    setLoading(true)
    try{
      const draftId = await ensureDraft()
      const form = new FormData()
      form.append('file', file)
      const res = await apiPost(`http://localhost:5000/api/submissions/${draftId}/files`, form, {
        headers:{Authorization:'Bearer '+token, 'Content-Type':'multipart/form-data'}
      })
      const savedFileNames = res.files.map(x=>x.filename)
      setCurrentFiles(prev => [...prev, ...savedFileNames])
      setMessage('Plik zapisany: ' + savedFileNames.join(', '))
      setFile(null)
    }catch(e){
      console.error(e)
      alert('Upload failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }finally{ setLoading(false) }
  }

  const submitForReview = async ()=>{
    if (!submissionId) return
    setLoading(true)
    try{
      await apiPost(`http://localhost:5000/api/submissions/${submissionId}/submit`, null, {headers:{Authorization:'Bearer '+token}})
      alert('Zgłoszenie przesłane do recenzji')
      setSubmissionId(null)
      setTitle(''); setAbstract(''); setAuthors(''); setCategory(''); setKeywordsText(''); setMessage('')
    }catch(e){
      console.error(e)
      alert('Submit failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }finally{ setLoading(false) }
  }

  return <div className="card">
    <h3>New submission</h3>
    <input placeholder="Title" value={title} onChange={e=>setTitle(e.target.value)} />
    <input placeholder="Authors (comma separated)" value={authors} onChange={e=>setAuthors(e.target.value)} />
    <input placeholder="Category / scientific field" value={category} onChange={e=>setCategory(e.target.value)} />
    <textarea placeholder="Abstract" value={abstract} onChange={e=>setAbstract(e.target.value)} />
    <input placeholder="Keywords (comma separated)" value={keywordsText} onChange={e=>setKeywordsText(e.target.value)} />
    <div style={{fontSize:13,color:'#555', marginBottom:8}}>Accepts PDF, DOCX, TEX files</div>
    <div style={{marginTop:16}}>
      <input type="file" accept=".pdf,.docx,.tex" onChange={e=>setFile(e.target.files?.[0]||null)} />
      <button onClick={uploadFile} disabled={loading} style={{marginLeft:8}}>{loading ? 'Uploading...' : 'Upload file'}</button>
    </div>
    <button onClick={createDraft} disabled={loading} style={{marginTop:12}}>{loading ? 'Saving...' : 'Save draft'}</button>
    {submissionId && <>
      {currentFiles.length > 0 && <div style={{marginTop:12}}>
        <strong>Przesłane pliki:</strong>
        {currentFiles.map(name => <div key={name} style={{fontSize:12}}>{name}</div>)}
        <div style={{fontSize:12,color:'#555',marginTop:4}}>Możesz przesłać nowy plik przed wysłaniem zgłoszenia do recenzji.</div>
      </div>}
      <div style={{marginTop:12}}>
        <button onClick={submitForReview} disabled={loading} style={{background:'#10b981'}}>{loading ? 'Submitting...' : 'Submit for review'}</button>
      </div>
    </>}
    {message && <div style={{marginTop:12,color:'#065f46'}}>{message}</div>}
  </div>
}

function MySubmissions({token}){
  const [list,setList]=useState([])
  const [loading,setLoading]=useState(false)
  const [editingId,setEditingId]=useState(null)
  const [editData,setEditData]=useState({title:'',abstract:'',authors:'',category:'',keywordsText:''})
  const [editFile,setEditFile]=useState(null)
  const [editFiles,setEditFiles]=useState([])

  useEffect(()=>{ fetchList() },[])

  const fetchList = async ()=>{
    setLoading(true)
    try{
      const res = await apiGet('http://localhost:5000/api/submissions',{headers:{Authorization:'Bearer '+token}})
      setList(res.submissions || [])
    }catch(e){ console.error(e); alert('Load failed') }finally{ setLoading(false) }
  }

  const startEdit = (s)=>{
    setEditingId(s.id)
    setEditData({
      title: s.title || '',
      abstract: s.abstract || '',
      authors: s.authors || '',
      category: s.category || '',
      keywordsText: (s.keywords || '').split(';').filter(Boolean).join(', ')
    })
    setEditFiles(s.files || [])
    setEditFile(null)
  }

  const saveEdit = async ()=>{
    try{
      const keywords = editData.keywordsText.split(',').map(k=>k.trim()).filter(Boolean)
      await apiPut(`http://localhost:5000/api/submissions/${editingId}`,{
        title: editData.title,
        abstract: editData.abstract,
        authors: editData.authors,
        category: editData.category,
        keywords
      },{headers:{Authorization:'Bearer '+token}})
      alert('Draft updated')
      setEditingId(null)
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Save failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  const submitDraft = async (id)=>{
    try{
      await apiPost(`http://localhost:5000/api/submissions/${id}/submit`, null, {headers:{Authorization:'Bearer '+token}})
      alert('Zgłoszenie wysłane do recenzji')
      await fetchList()
      setEditingId(null)
    }catch(e){
      console.error(e)
      alert('Submit failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  return <div className="card">
    <h3>My submissions</h3>
    {loading ? <div>Loading...</div> :
      list.length===0 ? <div>No submissions yet</div> :
      <>
        {list.map(s=> <div key={s.id} className="card" style={{marginBottom:8}}>
          <strong>{s.title}</strong>
          <div style={{fontSize:13,color:'#555'}}>{s.abstract}</div>
          <div style={{fontSize:12,color:'#777',marginTop:6}}>
            Authors: {s.authors || '—'}<br />
            Category: {s.category || '—'}<br />
            Keywords: {(s.keywords || '').replace(/;/g, ', ')}<br />
            Status: {statusLabel(s.status)} — Created: {new Date(s.createdAt).toLocaleString()}
          </div>
          {s.files && s.files.length > 0 && <div style={{marginTop:8, fontSize:12}}>
            <strong>Pliki:</strong>
            {s.files.map(file => <div key={file}>{file}</div>)}
          </div>}
          {s.assignments && s.assignments.length > 0 && <div style={{marginTop:8, fontSize:12}}>
            <strong>Przypisani recenzenci:</strong>
            {s.assignments.map(a => <div key={a.reviewerId}>{a.reviewerName || a.reviewerEmail}</div>)}
          </div>}
          {(s.status === 'draft' || s.status === 'rejected') && <div style={{marginTop:8}}>
            <button onClick={()=>startEdit(s)}>{s.status === 'rejected' ? 'Edit rejected submission' : 'Edit draft'}</button>
            <button onClick={()=>submitDraft(s.id)} style={{marginLeft:8, background:'#10b981'}}>{s.status === 'rejected' ? 'Resubmit' : 'Submit'}</button>
          </div>}
          {s.reviews && s.reviews.length > 0 && <div style={{marginTop:12}}>
            <strong>Oceny recenzentów:</strong>
            {s.reviews.map(r => <div key={r.id} style={{fontSize:12,color:'#333',marginTop:6}}>
              Ocena: {r.rating}/5<br />
              Komentarz: {r.content || 'Brak komentarza'}<br />
              Data: {new Date(r.createdAt).toLocaleString()}
            </div>)}
          </div>}
        </div>)}
      </>
    }
    {editingId && <div className="card" style={{marginTop:16}}>
      <h4>Edit draft</h4>
      <input placeholder="Title" value={editData.title} onChange={e=>setEditData({...editData,title:e.target.value})} />
      <input placeholder="Authors (comma separated)" value={editData.authors} onChange={e=>setEditData({...editData,authors:e.target.value})} />
      <input placeholder="Category / scientific field" value={editData.category} onChange={e=>setEditData({...editData,category:e.target.value})} />
      <textarea placeholder="Abstract" value={editData.abstract} onChange={e=>setEditData({...editData,abstract:e.target.value})} />
      <input placeholder="Keywords (comma separated)" value={editData.keywordsText} onChange={e=>setEditData({...editData,keywordsText:e.target.value})} />
      {editFiles.length > 0 && <div style={{marginTop:8, fontSize:12}}>
        <strong>Przesłane pliki:</strong>
        {editFiles.map(file => <div key={file}>{file}</div>)}
      </div>}
      <div style={{marginTop:12}}>
        <input type="file" accept=".pdf,.docx,.tex" onChange={e=>setEditFile(e.target.files?.[0]||null)} />
        <button onClick={async ()=>{
          if (!editFile) return alert('Select a file first')
          const form = new FormData()
          form.append('file', editFile)
          try{
            const res = await apiPost(`http://localhost:5000/api/submissions/${editingId}/files`, form, {
              headers:{Authorization:'Bearer '+token, 'Content-Type':'multipart/form-data'}
            })
            setEditFiles(prev => [...prev, ...res.files.map(x=>x.filename)])
            setEditFile(null)
            alert('Plik dodany do szkicu')
          }catch(e){
            console.error(e)
            alert('Upload failed: ' + (e.response?.data?.error || e.response?.status || e.message))
          }
        }} style={{marginLeft:8}}>Upload file</button>
      </div>
      <div style={{display:'flex', gap:8, marginTop:8}}>
        <button onClick={saveEdit}>Save draft</button>
        <button onClick={()=>setEditingId(null)} style={{background:'#6b7280'}}>Cancel</button>
      </div>
    </div>}
  </div>
}

function AssignedReviews({token}){
  const [list,setList]=useState([])
  const [loading,setLoading]=useState(false)
  const [drafts,setDrafts]=useState({})
  const [ratings,setRatings]=useState({})

  useEffect(()=>{ fetchList() },[token])

  const fetchList = async ()=>{
    setLoading(true)
    try{
      const res = await apiGet('http://localhost:5000/api/reviews/assigned',{headers:{Authorization:'Bearer '+token}})
      setList(res.assignments || [])
    }catch(e){ console.error(e); alert('Load failed') }finally{ setLoading(false) }
  }

  const submitReview = async (submissionId)=>{
    try{
      await apiPost(`http://localhost:5000/api/submissions/${submissionId}/reviews`,{
        content: drafts[submissionId] || '',
        rating: ratings[submissionId] || 3
      },{headers:{Authorization:'Bearer '+token}})
      alert('Review submitted')
      setDrafts(prev => ({ ...prev, [submissionId]: '' }))
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Submit failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  const submitDecision = async (submissionId, status)=>{
    try{
      await apiPost(`http://localhost:5000/api/submissions/${submissionId}/decision`, { status }, {headers:{Authorization:'Bearer '+token}})
      alert('Decision saved')
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Decision failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  return <div className="card">
    <h3>Assigned reviews</h3>
    {loading ? <div>Loading...</div> :
      list.length===0 ? <div>No assigned reviews</div> :
      list.map(item => <div key={item.submissionId} className="card" style={{marginBottom:8}}>
        <strong>{item.title}</strong>
        <div style={{fontSize:13,color:'#555'}}>{item.abstract}</div>
        <div style={{fontSize:12,color:'#777',marginTop:6}}>Status: {statusLabel(item.status)}</div>
        
        {/* Review form: Show only if status is submitted/in review AND no review yet */}
        {(item.status === 'submitted' || item.status === 'in review') && !item.myReview && 
          <div style={{marginTop:12}}>
            <div style={{fontSize:12,color:'#555',marginBottom:8}}>Add your review:</div>
            <textarea placeholder="Review comments" value={drafts[item.submissionId] || ''} onChange={e=>setDrafts({...drafts, [item.submissionId]: e.target.value})} />
            <div style={{display:'flex', gap:8, alignItems:'center', marginTop:8}}>
              <label>Rating: <input type="number" min={1} max={5} value={ratings[item.submissionId] || 3} onChange={e=>setRatings({...ratings, [item.submissionId]: Number(e.target.value)})} style={{width:60}} /></label>
              <button onClick={()=>submitReview(item.submissionId)} style={{background:'#2563eb',color:'white'}}>Submit review</button>
            </div>
          </div>
        }
        
        {/* Show existing review if present */}
        {item.myReview && <div style={{marginTop:12, padding:8, background:'#f0f0f0', borderRadius:4}}>
          <div style={{fontSize:12, fontWeight:'bold'}}>Your review:</div>
          <div style={{fontSize:12, marginTop:4}}>
            <strong>Rating:</strong> {item.myReview.rating}/5<br/>
            <strong>Comment:</strong> {item.myReview.content || 'No comment'}
          </div>
        </div>}
        
        {/* Decision buttons */}
        {item.status === 'in review' && item.myReview && 
          <div style={{marginTop:12, display:'flex', gap:8}}>
            <button onClick={()=>submitDecision(item.submissionId,'accepted')} style={{background:'#10b981',color:'white'}}>Accept</button>
            <button onClick={()=>submitDecision(item.submissionId,'rejected')} style={{background:'#ef4444',color:'white'}}>Reject</button>
          </div>
        }
        
        {/* Publish button: Only show if status is accepted */}
        {item.status === 'accepted' && 
          <div style={{marginTop:12}}>
            <button onClick={()=>submitDecision(item.submissionId,'published')} style={{background:'#10b981',color:'white'}}>Publish</button>
          </div>
        }
        
        {/* No actions for rejected or published */}
        {(item.status === 'rejected' || item.status === 'published') && 
          <div style={{marginTop:12, fontSize:12, color:'#666', fontStyle:'italic'}}>
            {item.status === 'rejected' ? 'This submission has been rejected.' : 'This submission has been published.'}
          </div>
        }
      </div>)
    }
  </div>
}

function Notifications({token}){
  const [list,setList]=useState([])
  const [loading,setLoading]=useState(false)

  useEffect(()=>{ fetchList() },[])

  const fetchList = async ()=>{
    setLoading(true)
    try{
      const res = await apiGet('http://localhost:5000/api/notifications',{headers:{Authorization:'Bearer '+token}})
      setList(res.notifications || [])
    }catch(e){ console.error(e); alert('Load failed') }finally{ setLoading(false) }
  }

  const markRead = async (id)=>{
    try{
      await apiPut(`http://localhost:5000/api/notifications/${id}/read`, null, {headers:{Authorization:'Bearer '+token}})
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Action failed')
    }
  }

  return <div className="card">
    <h3>Notifications</h3>
    {loading ? <div>Loading...</div> :
      list.length===0 ? <div>No notifications</div> :
      list.map(n=> <div key={n.id} className="card" style={{marginBottom:8, background: n.isRead ? '#f9fafb' : '#eef6ff'}}>
        <div>{n.message}</div>
        <div style={{fontSize:12,color:'#777',marginTop:6}}>{new Date(n.createdAt).toLocaleString()}</div>
        {!n.isRead && <button onClick={()=>markRead(n.id)} style={{marginTop:8}}>Mark read</button>}
      </div>)
    }
  </div>
}

function AdminSubmissions({token}){
  const [list,setList]=useState([])
  const [assignments,setAssignments]=useState([])
  const [reviews,setReviews]=useState([])
  const [reviewers,setReviewers]=useState([])
  const [selectedReviewer,setSelectedReviewer]=useState({})
  const [loading,setLoading]=useState(false)

  useEffect(()=>{ fetchList(); fetchReviewers() },[])

  const fetchList = async ()=>{
    setLoading(true)
    try{
      const res = await apiGet('http://localhost:5000/api/admin/submissions',{headers:{Authorization:'Bearer '+token}})
      setList(res.submissions || [])
      setAssignments(res.assignments || [])
      setReviews(res.reviews || [])
    }catch(e){ console.error(e); alert('Load failed (admin)') }finally{ setLoading(false) }
  }

  const fetchReviewers = async ()=>{
    try{
      const res = await apiGet('http://localhost:5000/api/admin/reviewers',{headers:{Authorization:'Bearer '+token}})
      setReviewers(res.reviewers || [])
    }catch(e){ console.error(e); }
  }

  const assignReviewer = async (submissionId)=>{
    const reviewerId = selectedReviewer[submissionId]
    if (!reviewerId) return alert('Select a reviewer')
    try{
      await apiPost(`http://localhost:5000/api/submissions/${submissionId}/assign-reviewer`, { reviewerId }, {headers:{Authorization:'Bearer '+token}})
      alert('Reviewer assigned')
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Assignment failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  const deleteSubmission = async (submissionId) => {
    if (!window.confirm('Are you sure you want to permanently delete this submission?')) return;
    try{
      await apiDelete(`http://localhost:5000/api/admin/submissions/${submissionId}`, {headers:{Authorization:'Bearer '+token}})
      alert('Submission deleted')
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Delete failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  const deleteReview = async (reviewId) => {
    if (!window.confirm('Are you sure you want to delete this review?')) return;
    try{
      await apiDelete(`http://localhost:5000/api/admin/reviews/${reviewId}`, {headers:{Authorization:'Bearer '+token}})
      alert('Review deleted')
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Delete failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  const submitDecision = async (submissionId, status)=>{
    try{
      await apiPost(`http://localhost:5000/api/submissions/${submissionId}/decision`, { status }, {headers:{Authorization:'Bearer '+token}})
      alert('Decision saved')
      await fetchList()
    }catch(e){
      console.error(e)
      alert('Decision failed: ' + (e.response?.data?.error || e.response?.status || e.message))
    }
  }

  const getAssigned = (submissionId) => assignments.filter(a => a.submissionId === submissionId)
  const getReviews = (submissionId) => reviews.filter(r => r.submissionId === submissionId)

  return <div className="card">
    <h3>All submissions (admin)</h3>
    {loading ? <div>Loading...</div> :
      list.length===0 ? <div>No submissions</div> :
      list.map(s=> <div key={s.id} className="card" style={{marginBottom:8}}>
        <strong>{s.title}</strong>
        <div style={{fontSize:13,color:'#555'}}>{s.abstract}</div>
        <div style={{fontSize:12,color:'#777',marginTop:6}}>
          Authors: {s.authors || '—'}<br />
          Category: {s.category || '—'}<br />
          Status: {statusLabel(s.status)}
        </div>
        <div style={{marginTop:8}}>
          <div>Assigned reviewers:</div>
          {getAssigned(s.id).length===0 ? <div style={{fontSize:12,color:'#777'}}>No reviewer assigned</div> :
            getAssigned(s.id).map(a => <div key={a.id} style={{fontSize:12,color:'#333'}}>{a.reviewerName || a.reviewerEmail} (assigned {new Date(a.assignedAt).toLocaleString()})</div>)}
        </div>
        {s.status === 'draft'
          ? <div style={{marginTop:8, fontSize:12, color:'#777'}}>Assigning reviewers and decisions are allowed only after submission.</div>
          : <div style={{display:'flex', alignItems:'center', gap:8, marginTop:8}}>
              <select value={selectedReviewer[s.id]||''} onChange={e=>setSelectedReviewer({...selectedReviewer, [s.id]: e.target.value})}>
                <option value="">Select reviewer</option>
                {reviewers.map(r=> <option key={r.id} value={r.id}>{r.name || r.email}</option>)}
              </select>
              <button onClick={()=>assignReviewer(s.id)}>Assign reviewer</button>
            </div>}

        {getReviews(s.id).length > 0 && <div style={{marginTop:12, fontSize:12}}>
          <strong>Reviews:</strong>
          {getReviews(s.id).map(r => <div key={r.id} style={{marginTop:8, padding:8, background:'#f9fafb', borderRadius:4}}>
            <div><strong>{r.reviewerName || r.reviewerEmail}</strong> — {r.rating}/5</div>
            <div style={{fontSize:12, marginTop:4}}>{r.content || 'No comment'}</div>
            <button onClick={()=>deleteReview(r.id)} style={{marginTop:8, background:'#ef4444', color:'white'}}>Delete review</button>
          </div>)}
        </div>}
        {s.status === 'published' ? <div style={{marginTop:8}}>
          <button onClick={()=>deleteSubmission(s.id)} style={{background:'#ef4444', color:'white'}}>Delete</button>
        </div> : s.status === 'accepted' || s.status === 'rejected' ? <div style={{marginTop:8}}>
          <button onClick={()=>submitDecision(s.id,'published')} style={{background:'#10b981'}}>Publish</button>
          <button onClick={()=>deleteSubmission(s.id)} style={{marginLeft:8, background:'#ef4444', color:'white'}}>Delete</button>
        </div> : <div style={{marginTop:8}}>
          <button onClick={()=>submitDecision(s.id,'accepted')}>Accept</button>
          <button onClick={()=>submitDecision(s.id,'rejected')} style={{marginLeft:8, background:'#ef4444'}}>Reject</button>
          <button onClick={()=>submitDecision(s.id,'published')} style={{marginLeft:8, background:'#10b981'}}>Publish</button>
        </div>}
      </div>)
    }
  </div>
}

function Statistics({token}){
  const [stats, setStats] = useState(null)
  const [loading, setLoading] = useState(false)

  useEffect(()=>{ fetchStats() }, [])

  const fetchStats = async ()=>{
    setLoading(true)
    try{
      const res = await apiGet('http://localhost:5000/api/statistics')
      setStats(res.data)
    }catch(e){ console.error(e); alert('Load failed') }finally{ setLoading(false) }
  }

  return <div className="card">
    <h3>Statystyki</h3>
    {loading ? <div>Loading...</div> :
      !stats ? <div>No data</div> :
      <div>
        <h4>Zgłoszenia</h4>
        <div style={{fontSize:14, marginBottom:8}}>
          <div>Razem: <strong>{stats.submissions.total}</strong></div>
          <div>Zgłoszone: <strong>{stats.submissions.submitted}</strong></div>
          <div>W recenzji: <strong>{stats.submissions.inReview}</strong></div>
          <div>Zaakceptowane: <strong>{stats.submissions.accepted}</strong></div>
          <div>Odrzucone: <strong>{stats.submissions.rejected}</strong></div>
          <div>Opublikowane: <strong>{stats.submissions.published}</strong></div>
        </div>
        <h4>Recenzje</h4>
        <div style={{fontSize:14}}>
          <div>Razem recenzji: <strong>{stats.reviews.total}</strong></div>
          <div>Średnia ocena: <strong>{stats.reviews.avgRating}/5</strong></div>
        </div>
      </div>
    }
  </div>
}
