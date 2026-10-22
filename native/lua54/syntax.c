#define LUA_CORE
#include "lua.h"
#include "lauxlib.h"
#include <string.h>

/* Compile text only. No standard libraries are opened and no chunk is executed. */
__declspec(dllexport) int __cdecl luma_lua_version(void) { return LUA_VERSION_RELEASE_NUM; }
__declspec(dllexport) int __cdecl luma_lua_check(const char *source, size_t length,
    const char *name, char *message, size_t capacity) {
    lua_State *state;
    int status;
    const char *error;
    if (!source || !message || capacity < 1 || length > 1024 * 1024) return -3;
    message[0] = 0;
    state = luaL_newstate();
    if (!state) return -2;
    status = luaL_loadbufferx(state, source, length, name, "t");
    if (status != LUA_OK) {
        size_t count;
        error = lua_tolstring(state, -1, &count);
        if (error) { if (count >= capacity) count = capacity - 1; memcpy(message, error, count); message[count] = 0; }
    }
    lua_close(state);
    return status;
}
