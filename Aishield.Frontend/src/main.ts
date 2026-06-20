import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ElementPlus from 'element-plus'
import zhCn from 'element-plus/es/locale/lang/zh-cn'
import 'element-plus/dist/index.css'
import './styles/main.css'
import App from './App.vue'
import router from './router'

const elementPlusLocale = {
  ...zhCn,
  el: {
    ...zhCn.el,
    pagination: {
      ...zhCn.el.pagination,
      total: '总条数：{total}条',
      pagesize: '条/页',
      goto: '转到',
    },
  },
}

createApp(App)
  .use(createPinia())
  .use(router)
  .use(ElementPlus, { locale: elementPlusLocale })
  .mount('#app')
