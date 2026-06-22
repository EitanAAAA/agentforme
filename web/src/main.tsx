import React from 'react'
import { createRoot } from 'react-dom/client'
import './styles.css'
import { DashboardPage } from './pages/DashboardPage'

createRoot(document.getElementById('app')!).render(
  <React.StrictMode>
    <DashboardPage />
  </React.StrictMode>,
)
