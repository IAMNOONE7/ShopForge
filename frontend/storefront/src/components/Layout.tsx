import { Link, NavLink, Outlet } from 'react-router'
import { getCategories, type Category } from '../api'
import { useCart } from '../cartContext'
import { useCustomer } from '../customerContext'
import { useStore } from '../storeContext'
import { useRequest } from '../useRequest'

export function Layout() {
  const store = useStore()
  const { cart } = useCart()
  const { customer } = useCustomer()
  const categories = useRequest('categories', getCategories)
  const categoryList: Category[] = categories.status === 'ready' ? categories.data : []

  return (
    <>
      <a href="#main-content" className="skip-link">
        Skip to content
      </a>
      <header className="store-header">
        <div className="store-header-inner container">
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
          <NavLink to={customer ? '/account' : '/account/sign-in'} className="account-link">
            {customer ? customer.firstName : 'Sign in'}
          </NavLink>
          <NavLink to="/cart" className="cart-link">
            Cart{cart && cart.count > 0 && <span className="cart-count">{cart.count}</span>}
          </NavLink>
        </div>
      </header>
      <main id="main-content" className="app container" tabIndex={-1}>
        <Outlet context={categoryList} />
      </main>
    </>
  )
}
