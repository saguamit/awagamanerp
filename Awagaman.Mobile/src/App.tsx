import { useEffect, useMemo, useState, type ReactNode } from 'react'

type AppUser = {
  id: number
  username: string
  fullName: string
  role: string
  isActive: boolean
  lastLoginUtc?: string | null
}

type LoginResponse = {
  token: string
  user: AppUser
}

type LedgerTab = 'challan' | 'lr' | 'bill' | 'tracking' | 'bill-summary'

type LedgerPageResult<T> = {
  items: T[]
  totalCount: number
  totalFreight?: number
  totalBalance?: number
  totalDue?: number
}

type TrackingPageResult<T> = {
  items: T[]
  totalCount: number
}

type BillPartyDueSummaryItem = {
  party: string
  bills: number
  due: number
}

type ChallanItem = {
  id: number
  challanNumber: string
  date: string
  lrNumber: string
  brokerName: string
  from: string
  to: string
  vehicleNumber: string
  vehicleType: string
  lorryHire: number
  billAmount: number
  margin: number
  due: number
}

type LRItem = {
  id: number
  lrNo: string
  date: string
  consignorName: string
  consigneeName: string
  from: string
  to: string
  vehicleNo: string
  vehicleType: string
  chNo: string
  totalFreight: number
  hamali: number
  detention: number
  others: number
  stCharge: number
  challanLorryHire: number
  billNo: string
  bill: number
  totalBill: number
  bal: number
}

type BillItem = {
  id: number
  billNo: string
  billDate: string
  party: string
  lrNo: string
  lrDate?: string | null
  from: string
  to: string
  total: number
  rcvd: number
  due: number
}

type TrackingItem = {
  id: number
  challanNo: string
  challanDate: string
  from: string
  to: string
  vehicleNo: string
  driverMobile: string
  ewayBillTillDate?: string | null
  dispatchDate?: string | null
  dispatchTime?: string | null
  deliveredDate?: string | null
  deliveredTime?: string | null
}

type TrackingLatestReportItem = {
  trackingEntryId: number
  reportDateTime?: string | null
  remarks: string
}

type TrackingReportItem = {
  id: number
  trackingEntryId: number
  reportDateTime: string
  remarks: string
}

const PAGE_SIZE = 10
const storageKey = 'awagaman-mobile-auth'

function getApiUrl(path: string) {
  const base = (import.meta.env.VITE_API_BASE_URL as string | undefined)?.trim()
  if (!base) {
    return path.startsWith('/') ? path : `/${path}`
  }

  return `${base.replace(/\/+$/, '')}/${path.replace(/^\/+/, '')}`
}

function toCamelCaseKey(value: string) {
  if (!value) return value
  if (value.toUpperCase() === value) {
    return value.toLowerCase()
  }

  let prefixLength = 0
  while (prefixLength < value.length && /[A-Z]/.test(value[prefixLength])) {
    prefixLength++
  }

  if (prefixLength <= 1) {
    return value.charAt(0).toLowerCase() + value.slice(1)
  }

  if (prefixLength < value.length && /[a-z]/.test(value[prefixLength])) {
    prefixLength--
  }

  return value.slice(0, prefixLength).toLowerCase() + value.slice(prefixLength)
}

function normalizeJson<T>(value: unknown): T {
  if (Array.isArray(value)) {
    return value.map((item) => normalizeJson(item)) as T
  }

  if (value && typeof value === 'object') {
    const result: Record<string, unknown> = {}
    for (const [key, item] of Object.entries(value as Record<string, unknown>)) {
      result[toCamelCaseKey(key)] = normalizeJson(item)
    }

    return result as T
  }

  return value as T
}

async function apiFetch<T>(path: string, token?: string, init?: RequestInit) {
  const response = await fetch(getApiUrl(path), {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: `Bearer ${token}` } : {}),
      ...(init?.headers ?? {}),
    },
  })

  if (!response.ok) {
    const text = await response.text()
    throw new Error(text || `Request failed: ${response.status}`)
  }

  return normalizeJson<T>(await response.json())
}

function formatDate(value?: string | null) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleDateString('en-GB', { day: '2-digit', month: 'short', year: 'numeric' })
}

function formatDateTime(value?: string | null) {
  if (!value) return '-'
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return value
  return date.toLocaleString('en-GB', {
    day: '2-digit',
    month: 'short',
    year: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function isPastDate(value?: string | null) {
  if (!value) return false
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return false
  const today = new Date()
  today.setHours(0, 0, 0, 0)
  date.setHours(0, 0, 0, 0)
  return date.getTime() < today.getTime()
}

function formatMoney(value?: number | null) {
  return `₹ ${(value ?? 0).toLocaleString('en-IN', { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`
}

function getStoredAuth() {
  try {
    const raw = localStorage.getItem(storageKey)
    if (!raw) return null
    return JSON.parse(raw) as { token: string; user: AppUser }
  } catch {
    return null
  }
}

function setStoredAuth(value: { token: string; user: AppUser } | null) {
  if (!value) {
    localStorage.removeItem(storageKey)
    return
  }

  localStorage.setItem(storageKey, JSON.stringify(value))
}

function sanitizePhoneNumber(value?: string | null) {
  return (value ?? '').replace(/\D/g, '')
}

function buildCallLink(value?: string | null) {
  const digits = sanitizePhoneNumber(value)
  return digits ? `tel:${digits}` : ''
}

function buildWhatsAppLink(value?: string | null) {
  const digits = sanitizePhoneNumber(value)
  if (!digits) return ''
  const normalized = digits.length === 10 ? `91${digits}` : digits
  return `https://wa.me/${normalized}`
}

function Detail({ label, value }: { label: string; value: string }) {
  return (
    <div className="detail-row">
      <span>{label}</span>
      <strong>{value}</strong>
    </div>
  )
}

function MetaChip({ children, tone = 'neutral' }: { children: ReactNode; tone?: 'neutral' | 'blue' | 'green' | 'amber' | 'red' }) {
  return <span className={`meta-chip meta-chip-${tone}`}>{children}</span>
}

function App() {
  const logoUrl = `${import.meta.env.BASE_URL}pwa-192.png`
  const initialAuth = useMemo(() => getStoredAuth(), [])

  const [token, setToken] = useState(initialAuth?.token ?? '')
  const [user, setUser] = useState<AppUser | null>(initialAuth?.user ?? null)
  const [username, setUsername] = useState('')
  const [password, setPassword] = useState('')
  const [loginBusy, setLoginBusy] = useState(false)
  const [loginError, setLoginError] = useState('')

  const [activeTab, setActiveTab] = useState<LedgerTab | null>(null)
  const [page, setPage] = useState(1)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  const [expandedId, setExpandedId] = useState<number | null>(null)
  const [hasSearched, setHasSearched] = useState(false)
  const [menuOpen, setMenuOpen] = useState(false)

  const [challanSearch, setChallanSearch] = useState({ q: '', broker: '', from: '', to: '' })
  const [lrSearch, setLrSearch] = useState({ q: '' })
  const [billSearch, setBillSearch] = useState({ q: '' })
  const [trackingSearch, setTrackingSearch] = useState({ q: '', from: '', to: '' })
  const [billSummarySearch, setBillSummarySearch] = useState('')

  const [challanData, setChallanData] = useState<LedgerPageResult<ChallanItem>>({ items: [], totalCount: 0, totalDue: 0 })
  const [lrData, setLrData] = useState<LedgerPageResult<LRItem>>({ items: [], totalCount: 0, totalFreight: 0, totalBalance: 0 })
  const [billData, setBillData] = useState<LedgerPageResult<BillItem>>({ items: [], totalCount: 0, totalDue: 0 })
  const [trackingData, setTrackingData] = useState<TrackingPageResult<TrackingItem>>({ items: [], totalCount: 0 })
  const [billSummaryData, setBillSummaryData] = useState<BillPartyDueSummaryItem[]>([])
  const [trackingLatestReports, setTrackingLatestReports] = useState<Record<number, TrackingLatestReportItem>>({})
  const [trackingReports, setTrackingReports] = useState<Record<number, TrackingReportItem[]>>({})
  const [trackingDrafts, setTrackingDrafts] = useState<Record<number, string>>({})
  const [trackingReportBusyId, setTrackingReportBusyId] = useState<number | null>(null)
  const [mobileActionId, setMobileActionId] = useState<number | null>(null)

  async function handleLogin(event: React.FormEvent) {
    event.preventDefault()
    setLoginError('')
    setLoginBusy(true)

    try {
      const result = await apiFetch<LoginResponse>('/api/auth/login', undefined, {
        method: 'POST',
        body: JSON.stringify({ username, password }),
      })

      setToken(result.token)
      setUser(result.user)
      setStoredAuth({ token: result.token, user: result.user })
      setUsername('')
      setPassword('')
    } catch (err) {
      setLoginError(err instanceof Error ? err.message : 'Login failed')
    } finally {
      setLoginBusy(false)
    }
  }

  function logout() {
    setMenuOpen(false)
    setToken('')
    setUser(null)
    setStoredAuth(null)
    setActiveTab(null)
    setPage(1)
    setExpandedId(null)
    setTrackingLatestReports({})
    setTrackingReports({})
    setTrackingDrafts({})
    setMobileActionId(null)
  }

  function currentTitle() {
    switch (activeTab) {
      case 'challan': return 'Challan'
      case 'lr': return 'LR'
      case 'bill': return 'Bill'
      case 'tracking': return 'Tracking'
      case 'bill-summary': return 'Bill Summary'
      default: return 'Home'
    }
  }

  function currentTotalCount() {
    switch (activeTab) {
      case 'challan': return challanData.totalCount
      case 'lr': return lrData.totalCount
      case 'bill': return billData.totalCount
      case 'tracking': return trackingData.totalCount
      case 'bill-summary': return billSummaryData.length
      default: return 0
    }
  }

  function currentLoadedCount() {
    switch (activeTab) {
      case 'challan': return challanData.items.length
      case 'lr': return lrData.items.length
      case 'bill': return billData.items.length
      case 'tracking': return trackingData.items.length
      case 'bill-summary': return billSummaryData.length
      default: return 0
    }
  }

  async function runSearch(nextTab = activeTab, nextPage = page, append = false) {
    if (!token || !nextTab) return

    setBusy(true)
    setError('')
    setExpandedId(null)
    setHasSearched(true)

    try {
      if (nextTab === 'challan') {
        const result = await apiFetch<LedgerPageResult<ChallanItem>>(
          `/api/challans/ledger-page?page=${nextPage}&pageSize=${PAGE_SIZE}&sort=ChallanNumber&asc=false&ledgerKind=challan&search=${encodeURIComponent(challanSearch.q)}&broker=${encodeURIComponent(challanSearch.broker)}&from=${encodeURIComponent(challanSearch.from)}&to=${encodeURIComponent(challanSearch.to)}`,
          token,
        )
        setChallanData((current) => append ? { ...result, items: [...current.items, ...result.items] } : result)
      } else if (nextTab === 'lr') {
        const result = await apiFetch<LedgerPageResult<LRItem>>(
          `/api/lr/ledger-page?page=${nextPage}&pageSize=${PAGE_SIZE}&sort=LRNo&asc=false&search=${encodeURIComponent(lrSearch.q)}`,
          token,
        )
        setLrData((current) => append ? { ...result, items: [...current.items, ...result.items] } : result)
      } else if (nextTab === 'bill') {
        const result = await apiFetch<LedgerPageResult<BillItem>>(
          `/api/bills/page?page=${nextPage}&pageSize=${PAGE_SIZE}&sort=BillNo&asc=false&search=${encodeURIComponent(billSearch.q)}`,
          token,
        )
        setBillData((current) => append ? { ...result, items: [...current.items, ...result.items] } : result)
      } else if (nextTab === 'tracking') {
        const result = await apiFetch<TrackingPageResult<TrackingItem>>(
          `/api/tracking/page?page=${nextPage}&pageSize=${PAGE_SIZE}&search=${encodeURIComponent(trackingSearch.q)}&from=${encodeURIComponent(trackingSearch.from)}&to=${encodeURIComponent(trackingSearch.to)}`,
          token,
        )
        setTrackingData((current) => append ? { ...result, items: [...current.items, ...result.items] } : result)
        const latestReports = await apiFetch<TrackingLatestReportItem[]>('/api/tracking/latest-reports', token)
        const reportMap: Record<number, TrackingLatestReportItem> = {}
        for (const item of latestReports) {
          reportMap[item.trackingEntryId] = item
        }
        setTrackingLatestReports(reportMap)
      } else {
        const result = await apiFetch<BillPartyDueSummaryItem[]>('/api/bills/party-due-summary', token)
        const filtered = result.filter((item) =>
          !billSummarySearch.trim() ||
          item.party.toLowerCase().includes(billSummarySearch.trim().toLowerCase()))
        setBillSummaryData(filtered)
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load records')
    } finally {
      setBusy(false)
    }
  }

  async function loadTrackingReports(trackingEntryId: number) {
    if (!token || trackingEntryId <= 0) return

    try {
      const reports = await apiFetch<TrackingReportItem[]>(`/api/tracking/${trackingEntryId}/reports`, token)
      setTrackingReports((current) => ({ ...current, [trackingEntryId]: reports }))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load tracking updates')
    }
  }

  async function toggleTrackingDetails(item: TrackingItem) {
    const isClosing = expandedId === item.id
    setExpandedId(isClosing ? null : item.id)
    setMobileActionId(null)

    if (!isClosing && !trackingReports[item.id]) {
      await loadTrackingReports(item.id)
    }
  }

  async function submitTrackingUpdate(item: TrackingItem) {
    if (!token || item.id <= 0) return

    const remarks = (trackingDrafts[item.id] ?? '').trim()
    if (!remarks) {
      setError('Enter tracking update remarks first.')
      return
    }

    setTrackingReportBusyId(item.id)
    setError('')
    const reportDateTime = new Date().toISOString()

    try {
      const created = await apiFetch<{ id: number }>(`/api/tracking/${item.id}/reports`, token, {
        method: 'POST',
        body: JSON.stringify({
          trackingEntryId: item.id,
          reportDateTime,
          remarks,
        }),
      })

      const report: TrackingReportItem = {
        id: created.id,
        trackingEntryId: item.id,
        reportDateTime,
        remarks,
      }

      setTrackingDrafts((current) => ({ ...current, [item.id]: '' }))
      setTrackingReports((current) => ({
        ...current,
        [item.id]: [...(current[item.id] ?? []), report],
      }))
      setTrackingLatestReports((current) => ({
        ...current,
        [item.id]: {
          trackingEntryId: item.id,
          reportDateTime,
          remarks,
        },
      }))
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to save tracking update')
    } finally {
      setTrackingReportBusyId(null)
    }
  }

  function openSection(tab: LedgerTab) {
    setMenuOpen(false)
    setActiveTab(tab)
    setPage(1)
    setExpandedId(null)
    setError('')
    void runSearch(tab, 1, false)
  }

  useEffect(() => {
    const closeMenu = () => setMenuOpen(false)
    window.addEventListener('scroll', closeMenu)
    return () => window.removeEventListener('scroll', closeMenu)
  }, [])

  useEffect(() => {
    const onPopState = () => {
      if (expandedId !== null) {
        setExpandedId(null)
        return
      }

      setMenuOpen(false)
      if (activeTab !== null) {
        setActiveTab(null)
      }
    }

    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [activeTab, expandedId])

  useEffect(() => {
    if (activeTab !== null) {
      window.history.pushState({ screen: activeTab }, '')
    }
  }, [activeTab])

  if (!token || !user) {
    return (
      <div className="mobile-shell">
        <div className="login-card">
          <img src={logoUrl} alt="Awagaman ERP" className="login-logo" />
          <h1>Awagaman Mobile</h1>
          <p>Read-only mobile search for challan, LR, bill and tracking ledgers.</p>

          <form onSubmit={handleLogin} className="login-form">
            <input value={username} onChange={(e) => setUsername(e.target.value)} placeholder="Username" autoComplete="username" />
            <input value={password} onChange={(e) => setPassword(e.target.value)} placeholder="Password" type="password" autoComplete="current-password" />
            <button type="submit" disabled={loginBusy}>{loginBusy ? 'Signing in...' : 'Sign In'}</button>
          </form>

          {loginError ? <div className="error-box">{loginError}</div> : null}
        </div>
      </div>
    )
  }

  return (
    <div className="mobile-shell">
      <header className="topbar sticky-card">
        <button type="button" className="menu-icon-button" onClick={() => setMenuOpen((value) => !value)} aria-label="Open menu" aria-expanded={menuOpen}>
          ☰
        </button>
        <div className="section-title">{currentTitle()}</div>
        <div className="topbar-spacer" />
        {menuOpen ? (
          <div className="topbar-menu">
            <div className="menu-brand">Awagaman Mobile</div>
            <div className="menu-user">{user.fullName || user.username}</div>
            <button type="button" className="menu-item" onClick={() => { setActiveTab(null); setMenuOpen(false) }}>Home</button>
            <button type="button" className="menu-item" onClick={logout}>Logout</button>
          </div>
        ) : null}
      </header>

      {!activeTab ? (
        <section className="home-grid">
          <button type="button" className="home-card" onClick={() => openSection('challan')}>
            <strong>Challan</strong>
            <span>Search by challan, broker, from and to</span>
          </button>
          <button type="button" className="home-card" onClick={() => openSection('lr')}>
            <strong>LR</strong>
            <span>Search LR, consignor, consignee and challan</span>
          </button>
          <button type="button" className="home-card" onClick={() => openSection('bill')}>
            <strong>Bill</strong>
            <span>Search bill, party and LR</span>
          </button>
          <button type="button" className="home-card" onClick={() => openSection('tracking')}>
            <strong>Tracking</strong>
            <span>Search challan movement and route</span>
          </button>
          <button type="button" className="home-card" onClick={() => openSection('bill-summary')}>
            <strong>Bill Summary</strong>
            <span>Party wise due summary</span>
          </button>
        </section>
      ) : null}

      {activeTab === 'challan' ? (
        <section className="search-panel">
          <div className="filter-grid">
            <input value={challanSearch.q} onChange={(e) => setChallanSearch({ ...challanSearch, q: e.target.value })} placeholder="Challan / LR / vehicle" />
            <input value={challanSearch.broker} onChange={(e) => setChallanSearch({ ...challanSearch, broker: e.target.value })} placeholder="Broker" />
            <input value={challanSearch.from} onChange={(e) => setChallanSearch({ ...challanSearch, from: e.target.value })} placeholder="From" />
            <input value={challanSearch.to} onChange={(e) => setChallanSearch({ ...challanSearch, to: e.target.value })} placeholder="To" />
          </div>
          <div className="action-row">
            <button type="button" onClick={() => { setPage(1); void runSearch('challan', 1, false) }} disabled={busy}>{busy ? 'Loading...' : 'Search'}</button>
            <button type="button" className="secondary-button" onClick={() => { setChallanSearch({ q: '', broker: '', from: '', to: '' }); setPage(1); void runSearch('challan', 1, false) }}>Clear</button>
          </div>
        </section>
      ) : null}

      {activeTab === 'lr' ? (
        <section className="search-panel">
          <div className="filter-grid single">
            <input value={lrSearch.q} onChange={(e) => setLrSearch({ q: e.target.value })} placeholder="LR / consignor / consignee / challan" />
          </div>
          <div className="action-row">
            <button type="button" onClick={() => { setPage(1); void runSearch('lr', 1, false) }} disabled={busy}>{busy ? 'Loading...' : 'Search'}</button>
            <button type="button" className="secondary-button" onClick={() => { setLrSearch({ q: '' }); setPage(1); void runSearch('lr', 1, false) }}>Clear</button>
          </div>
        </section>
      ) : null}

      {activeTab === 'bill' ? (
        <section className="search-panel">
          <div className="filter-grid single">
            <input value={billSearch.q} onChange={(e) => setBillSearch({ q: e.target.value })} placeholder="Bill / party / LR" />
          </div>
          <div className="action-row">
            <button type="button" onClick={() => { setPage(1); void runSearch('bill', 1, false) }} disabled={busy}>{busy ? 'Loading...' : 'Search'}</button>
            <button type="button" className="secondary-button" onClick={() => { setBillSearch({ q: '' }); setPage(1); void runSearch('bill', 1, false) }}>Clear</button>
          </div>
        </section>
      ) : null}

      {activeTab === 'tracking' ? (
        <section className="search-panel">
          <div className="filter-grid">
            <input value={trackingSearch.q} onChange={(e) => setTrackingSearch({ ...trackingSearch, q: e.target.value })} placeholder="Challan / vehicle / mobile" />
            <input value={trackingSearch.from} onChange={(e) => setTrackingSearch({ ...trackingSearch, from: e.target.value })} placeholder="From" />
            <input value={trackingSearch.to} onChange={(e) => setTrackingSearch({ ...trackingSearch, to: e.target.value })} placeholder="To" />
          </div>
          <div className="action-row">
            <button type="button" onClick={() => { setPage(1); void runSearch('tracking', 1, false) }} disabled={busy}>{busy ? 'Loading...' : 'Search'}</button>
            <button type="button" className="secondary-button" onClick={() => { setTrackingSearch({ q: '', from: '', to: '' }); setPage(1); void runSearch('tracking', 1, false) }}>Clear</button>
          </div>
        </section>
      ) : null}

      {activeTab === 'bill-summary' ? (
        <section className="search-panel">
          <div className="filter-grid single">
            <input value={billSummarySearch} onChange={(e) => setBillSummarySearch(e.target.value)} placeholder="Search party" />
          </div>
          <div className="action-row">
            <button type="button" onClick={() => { void runSearch('bill-summary', 1, false) }} disabled={busy}>{busy ? 'Loading...' : 'Search'}</button>
            <button type="button" className="secondary-button" onClick={() => { setBillSummarySearch(''); void runSearch('bill-summary', 1, false) }}>Clear</button>
          </div>
        </section>
      ) : null}

      {activeTab ? <div className="result-count">{hasSearched ? `${currentLoadedCount()} of ${currentTotalCount()} records` : 'Loading records...'}</div> : null}
      {busy ? <div className="status-box">Loading...</div> : null}
      {error ? <div className="error-box">{error}</div> : null}

      {activeTab ? (
        <section className="results-list">
          {activeTab === 'challan' && challanData.items.map((item) => (
            <article className="ledger-card" key={item.id}>
              <button className="ledger-head" onClick={() => setExpandedId(expandedId === item.id ? null : item.id)}>
                <div className="ledger-head-main">
                  <strong>{item.challanNumber || '-'}</strong>
                  <span>{formatDate(item.date)}</span>
                </div>
                <div className="ledger-head-side">
                  <div className="ledger-amount">{formatMoney(item.due)}</div>
                  <div className="ledger-expand">{expandedId === item.id ? 'Less' : 'Details'}</div>
                </div>
              </button>
              <div className="ledger-meta">
                <MetaChip tone="blue">{item.brokerName || 'No Broker'}</MetaChip>
                <MetaChip tone="green">{item.from || '-'}</MetaChip>
                <MetaChip tone="amber">{item.to || '-'}</MetaChip>
              </div>
              {expandedId === item.id ? (
                <div className="ledger-details ledger-table">
                  <Detail label="LR" value={item.lrNumber || '-'} />
                  <Detail label="Vehicle" value={`${item.vehicleNumber || '-'} ${item.vehicleType ? `(${item.vehicleType})` : ''}`} />
                  <Detail label="Lorry Hire" value={formatMoney(item.lorryHire)} />
                  <Detail label="Bill Amount" value={formatMoney(item.billAmount)} />
                  <Detail label="Margin" value={formatMoney(item.margin)} />
                  <Detail label="Due" value={formatMoney(item.due)} />
                </div>
              ) : null}
            </article>
          ))}

          {activeTab === 'lr' && lrData.items.map((item) => (
            <article className="ledger-card" key={item.id}>
              <button className="ledger-head" onClick={() => setExpandedId(expandedId === item.id ? null : item.id)}>
                <div className="ledger-head-main">
                  <strong>{item.lrNo || '-'}</strong>
                  <span>{formatDate(item.date)}</span>
                </div>
                <div className="ledger-head-side">
                  <div className="ledger-amount">{formatMoney(item.totalBill)}</div>
                  <div className="ledger-expand">{expandedId === item.id ? 'Less' : 'Details'}</div>
                </div>
              </button>
              <div className="ledger-meta">
                <MetaChip tone="blue">{item.consignorName || '-'}</MetaChip>
                <MetaChip tone="amber">{item.consigneeName || '-'}</MetaChip>
              </div>
              {expandedId === item.id ? (
                <div className="ledger-details ledger-table">
                  <Detail label="Route" value={`${item.from || '-'} → ${item.to || '-'}`} />
                  <Detail label="Challan" value={item.chNo || '-'} />
                  <Detail label="Vehicle" value={`${item.vehicleNo || '-'} ${item.vehicleType ? `(${item.vehicleType})` : ''}`} />
                  <Detail label="Lorry Hire" value={formatMoney(item.challanLorryHire)} />
                  <Detail label="Total Freight" value={formatMoney(item.totalFreight)} />
                  <Detail label="Detention" value={formatMoney(item.detention)} />
                  <Detail label="Hamali" value={formatMoney(item.hamali)} />
                  <Detail label="Other" value={formatMoney(item.others)} />
                  <Detail label="ST Charge" value={formatMoney(item.stCharge)} />
                  <Detail label="Total Bill" value={formatMoney(item.totalBill)} />
                  <Detail label="Bill No" value={item.billNo || '-'} />
                  <Detail label="Bill Amount" value={formatMoney(item.bill)} />
                  <Detail label="Balance" value={formatMoney(item.bal)} />
                </div>
              ) : null}
            </article>
          ))}

          {activeTab === 'bill' && billData.items.map((item) => (
            <article className="ledger-card" key={item.id}>
              <button className="ledger-head" onClick={() => setExpandedId(expandedId === item.id ? null : item.id)}>
                <div className="ledger-head-main">
                  <strong>{item.billNo || '-'}</strong>
                  <span>{formatDate(item.billDate)}</span>
                </div>
                <div className="ledger-head-side">
                  <div className="ledger-amount">{formatMoney(item.due)}</div>
                  <div className="ledger-expand">{expandedId === item.id ? 'Less' : 'Details'}</div>
                </div>
              </button>
              <div className="ledger-meta">
                <MetaChip tone="blue">{item.party || '-'}</MetaChip>
                <MetaChip tone="green">LR {item.lrNo || '-'}</MetaChip>
              </div>
              {expandedId === item.id ? (
                <div className="ledger-details ledger-table">
                  <Detail label="LR Date" value={formatDate(item.lrDate)} />
                  <Detail label="Route" value={`${item.from || '-'} → ${item.to || '-'}`} />
                  <Detail label="Total" value={formatMoney(item.total)} />
                  <Detail label="Received" value={formatMoney(item.rcvd)} />
                  <Detail label="Due" value={formatMoney(item.due)} />
                </div>
              ) : null}
            </article>
          ))}

          {activeTab === 'tracking' && trackingData.items.map((item) => (
            <article className="ledger-card" key={item.id}>
              <button className="ledger-head" onClick={() => void toggleTrackingDetails(item)}>
                <div className="ledger-head-main">
                  <strong>{item.challanNo || '-'}</strong>
                  <span>{formatDate(item.challanDate)}</span>
                </div>
                <div className="ledger-head-side">
                  <div className="ledger-expand">{expandedId === item.id ? 'Less' : 'Details'}</div>
                </div>
              </button>
              <div className="ledger-meta">
                <MetaChip tone="green">{item.from || '-'}</MetaChip>
                <MetaChip tone="amber">{item.to || '-'}</MetaChip>
                <MetaChip>{item.vehicleNo || '-'}</MetaChip>
                {item.ewayBillTillDate ? (
                  <MetaChip tone={isPastDate(item.ewayBillTillDate) ? 'red' : 'neutral'}>
                    E-Way {formatDate(item.ewayBillTillDate)}
                  </MetaChip>
                ) : null}
                {trackingLatestReports[item.id]?.remarks ? <MetaChip tone="blue">Updated</MetaChip> : null}
              </div>
              {expandedId === item.id ? (
                <div className="ledger-details ledger-table">
                  <button
                    type="button"
                    className="detail-row detail-button"
                    onClick={() => setMobileActionId(mobileActionId === item.id ? null : item.id)}
                    disabled={!item.driverMobile}
                  >
                    <span>Driver Mobile</span>
                    <strong>{item.driverMobile || '-'}</strong>
                  </button>
                  {mobileActionId === item.id && item.driverMobile ? (
                    <div className="inline-action-row">
                      <a className="quick-link call-link" href={buildCallLink(item.driverMobile)}>Call</a>
                      <a className="quick-link whatsapp-link" href={buildWhatsAppLink(item.driverMobile)} target="_blank" rel="noreferrer">WhatsApp</a>
                    </div>
                  ) : null}
                  <Detail label="Dispatch" value={`${formatDate(item.dispatchDate)} ${item.dispatchTime || ''}`.trim() || '-'} />
                  <Detail label="Delivered" value={`${formatDate(item.deliveredDate)} ${item.deliveredTime || ''}`.trim() || '-'} />
                  <Detail label="Eway Till" value={formatDate(item.ewayBillTillDate)} />
                  {isPastDate(item.ewayBillTillDate) ? (
                    <div className="tracking-expired-note">E-Way bill expired</div>
                  ) : null}
                  <Detail label="Latest Update" value={trackingLatestReports[item.id]?.remarks || '-'} />
                  <Detail label="Updated At" value={formatDateTime(trackingLatestReports[item.id]?.reportDateTime)} />
                  <div className="tracking-update-box">
                    <label className="tracking-update-label" htmlFor={`tracking-update-${item.id}`}>Add update</label>
                    <textarea
                      id={`tracking-update-${item.id}`}
                      value={trackingDrafts[item.id] ?? ''}
                      onChange={(e) => setTrackingDrafts((current) => ({ ...current, [item.id]: e.target.value }))}
                      placeholder="Enter tracking update"
                      rows={3}
                    />
                    <button
                      type="button"
                      className="tracking-update-button"
                      disabled={trackingReportBusyId === item.id}
                      onClick={() => void submitTrackingUpdate(item)}
                    >
                      {trackingReportBusyId === item.id ? 'Saving...' : 'Save Update'}
                    </button>
                  </div>
                  <div className="tracking-report-list">
                    <div className="tracking-report-title">Updates</div>
                    {(trackingReports[item.id] ?? []).length > 0 ? (
                      [...(trackingReports[item.id] ?? [])]
                        .sort((left, right) => new Date(right.reportDateTime).getTime() - new Date(left.reportDateTime).getTime())
                        .map((report) => (
                          <div className="tracking-report-item" key={report.id}>
                            <strong>{formatDateTime(report.reportDateTime)}</strong>
                            <span>{report.remarks || '-'}</span>
                          </div>
                        ))
                    ) : (
                      <div className="tracking-report-empty">No updates yet.</div>
                    )}
                  </div>
                </div>
              ) : null}
            </article>
          ))}

          {activeTab === 'bill-summary' && billSummaryData.map((item, index) => (
            <article className="ledger-card" key={`${item.party}-${index}`}>
              <button className="ledger-head ledger-head-static" type="button">
                <div className="ledger-head-main">
                  <strong>{item.party || '-'}</strong>
                  <span>{item.bills} bills</span>
                </div>
                <div className="ledger-head-side">
                  <div className="ledger-amount">{formatMoney(item.due)}</div>
                </div>
              </button>
              <div className="ledger-meta">
                <MetaChip tone="blue">{item.bills} Bills</MetaChip>
                <MetaChip tone="amber">Due</MetaChip>
              </div>
            </article>
          ))}

          {hasSearched && !busy && currentTotalCount() === 0 ? <div className="empty-box">No records found.</div> : null}
        </section>
      ) : null}

      {activeTab && activeTab !== 'bill-summary' && hasSearched && currentTotalCount() > 0 ? (
        <footer className="pager">
          <button
            disabled={page <= 1 || busy}
            onClick={() => {
              setPage(1)
              void runSearch(activeTab, 1, false)
              window.scrollTo({ top: 0, behavior: 'smooth' })
            }}
          >
            Go First
          </button>
          <span>{currentLoadedCount()} / {currentTotalCount()}</span>
          <button
            disabled={currentLoadedCount() >= currentTotalCount() || busy}
            onClick={() => {
              const next = page + 1
              setPage(next)
              void runSearch(activeTab, next, true)
            }}
          >
            More Records
          </button>
        </footer>
      ) : null}
    </div>
  )
}

export default App
