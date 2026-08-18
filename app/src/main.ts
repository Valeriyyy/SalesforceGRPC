import { mount } from 'svelte'
import './assets/css/app.css'
import App from './pages/App.svelte'

const app = mount(App, {
  target: document.getElementById('app')!,
})

export default app
