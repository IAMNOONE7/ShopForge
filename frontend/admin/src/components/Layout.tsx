import { NavLink, Outlet } from 'react-router'
import { api } from '../api'
import { useSession } from '../session'
import { useRequest } from '../useRequest'

export function Layout() {
  const { user, logout } = useSession()
  const [stores, reloadStores] = useRequest('stores', api.stores)

  return (
    <>
      <a href="#main-content" className="skip-link">
        Skip to content
      </a>
      <header className="admin-header">
        <div className="admin-header-inner container">
          <strong>ShopForge Admin</strong>
          <nav aria-label="Administration">
            <NavLink to="/products">Products</NavLink>
            <NavLink to="/stores/new">New store</NavLink>
            {stores.status === 'ready' &&
              stores.data.map((store) => (
                <NavLink key={store.id} to={`/stores/${store.id}`}>
                  {store.name}
                  {store.status === 'draft' && <span className="badge">draft</span>}
                </NavLink>
              ))}
          </nav>
          <span className="admin-user">
            {user.email} ({user.role})
            <button type="button" onClick={logout}>
              Sign out
            </button>
          </span>
        </div>
      </header>
      <main id="main-content" className="app container" tabIndex={-1}>
        <Outlet context={{ stores: stores.status === 'ready' ? stores.data : [], reloadStores }} />
      </main>
    </>
  )
}
