window.hatchlyStorage = {
    get: key => {
        try {
            return window.localStorage.getItem(key);
        } catch {
            return null;
        }
    },
    set: (key, value) => {
        try {
            window.localStorage.setItem(key, value);
            return true;
        } catch {
            return false;
        }
    },
    remove: key => {
        try {
            window.localStorage.removeItem(key);
            return true;
        } catch {
            return false;
        }
    }
};
