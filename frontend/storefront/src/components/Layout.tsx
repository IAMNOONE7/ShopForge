import { Link, NavLink, Outlet } from 'react-router'
import { getCategories, type Category } from '../api'
import { useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function Layout() {
  const store = useStore()
  const categories = useRequest('categories', getCategories)
  const categoryList: Category[] = categories.status === 'ready' ? categories.data : []

  return (
    <>
      <header className="store-header">
        <div className="store-header-inner">
          <Link to="/" className="store-brand">
            {store.logoUrl ? <img src={store.logoUrl} alt={store.name} className="store-logo" /> : store.name}
          </Link>
          <nav className="store-nav">
            <NavLink to="/" end>
              All products
            </NavLink>
            {categoryList.map((category) => (
              <NavLink key={category.slug} to={`/c/${category.slug}`}>
                {category.name}
              </NavLink>
            ))}
          </nav>
        </div>
      </header>
      <main className="app">
        <Outlet context={categoryList} />
      </main>
    </>
  )
}
