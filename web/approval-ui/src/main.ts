import { createApp } from 'vue'
import ElementPlus from 'element-plus'
import 'element-plus/dist/index.css'
import '@vue-flow/core/dist/style.css'
import '@vue-flow/core/dist/theme-default.css'
import '@vue-flow/controls/dist/style.css'
import './styles/main.css'
import App from './App.vue'
import { router } from './router'

createApp(App).use(ElementPlus).use(router).mount('#app')
